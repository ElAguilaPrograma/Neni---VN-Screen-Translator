using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;

namespace Neni.Presentation.ViewModels;

// Maneja el preview en vivo (pull de frames + WriteableBitmap) y el estado de las ROIs mientras
// RoiSelectionWindow está abierta. La conversión punto-de-pantalla <-> píxel-de-frame vive acá
// para que el code-behind de la ventana (que solo conoce gestos de puntero y el árbol visual) no
// tenga que saber nada de Stretch="Uniform"/letterboxing.
public partial class RoiSelectionViewModel : ViewModelBase, IDisposable
{
    // Medido en vivo contra este pipeline (ver sesión de debugging): videoconvert convirtiendo a
    // BGRA a resolución de pantalla completa apenas sostiene ~0.5-1 fps reales -- pollear cada
    // 33ms no lo acelera, solo le quita CPU al propio hilo de conversión de GStreamer (cada pull
    // fallido bloquea igual hasta 50ms en gst_app_sink_try_pull_sample). 250ms es un intervalo
    // mucho más barato que sigue sintiéndose "vivo" para dibujar ROIs.
    private const int PreviewIntervalMs = 250;
    private const double MinDragSizePx = 4; // Por debajo de esto se trata como un clic accidental, no un arrastre.

    private readonly IFrameCapture _frameCapture;
    private readonly DispatcherTimer _previewTimer;
    private readonly int _maxRois;
    private bool _isPulling;
    private bool _isDragging;
    private int _nextDraftId;
    private int _frameWidth;
    private int _frameHeight;

    [ObservableProperty]
    public partial WriteableBitmap? PreviewBitmap { get; set; }

    [ObservableProperty]
    public partial string CounterText { get; set; } = "";

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    // Diagnóstico: cuántas veces se llamó a GrabFrameAsync en total.
    [ObservableProperty]
    public partial int PullCount { get; set; }

    // Diagnóstico: de esos pulls, cuántos trajeron un Frame REALMENTE nuevo (no el mismo
    // _lastFrame cacheado que ya se había mostrado). Si PullCount sube pero esto no, confirma que
    // GStreamer no está entregando nada nuevo — el problema está río arriba, no en el render.
    [ObservableProperty]
    public partial int NewFrameCount { get; set; }

    // LinuxFrameCapture devuelve la MISMA instancia de Frame cuando no hay sample nuevo (reusa el
    // _lastFrame cacheado); comparar por referencia nos deja saltarnos por completo la creación
    // del WriteableBitmap + copia de ~15MB cuando no hay nada distinto que mostrar. Es la pieza
    // clave del fix: hacer ese trabajo en cada tick le quitaba CPU justo al hilo de videoconvert,
    // que ya de por sí apenas alcanza a convertir ~1 frame por segundo a esta resolución.
    private Frame? _lastAppliedFrame;

    public ObservableCollection<RoiDraftItem> Items { get; } = new();

    // null hasta que se presiona "Confirmar" — RegionOfInterest (el servicio) lo lee después de
    // que la ventana cierra para distinguir "confirmó" de "cerró con la X sin confirmar".
    public IReadOnlyList<RegionOfInterest>? ConfirmedRois { get; private set; }

    public RoiSelectionViewModel(
        Frame initialFrame,
        IFrameCapture frameCapture,
        int maxRois,
        IEnumerable<RegionOfInterest> existingRois)
    {
        _frameCapture = frameCapture;
        _maxRois = maxRois;
        _frameWidth = initialFrame.Width;
        _frameHeight = initialFrame.Height;

        foreach (var roi in existingRois)
            Items.Add(new RoiDraftItem(roi.RoiId, roi.X, roi.Y, roi.W, roi.H, (decimal)roi.Scale));

        _nextDraftId = Items.Count == 0 ? 1 : Items.Max(i => i.RoiId) + 1;
        Items.CollectionChanged += (_, _) => UpdateCounter();
        UpdateCounter();

        PreviewBitmap = CreateBitmap(initialFrame);
        WriteInto(PreviewBitmap, initialFrame);

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PreviewIntervalMs) };
        _previewTimer.Tick += async (_, _) => await PullFrameAsync();
        _previewTimer.Start();
    }

    public bool CanAddRoi => Items.Count < _maxRois;

    // Traduce un punto de arrastre (en espacio del Canvas de overlay) a una nueva ROI en espacio
    // de píxel del frame. No hace nada si ya se llegó al tope de ROIs o si el arrastre fue
    // demasiado pequeño (clic accidental).
    public void CommitDrag(Point start, Point end, Size canvasSize)
    {
        if (!CanAddRoi)
        {
            StatusMessage = $"Ya hay {_maxRois} ROIs definidas, el máximo permitido.";
            return;
        }

        var a = ToFrameSpace(start, canvasSize);
        var b = ToFrameSpace(end, canvasSize);

        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var w = Math.Abs(b.X - a.X);
        var h = Math.Abs(b.Y - a.Y);

        if (w < MinDragSizePx || h < MinDragSizePx)
            return;

        Items.Add(new RoiDraftItem(_nextDraftId++, x, y, w, h, 1.0m));
        StatusMessage = null;
    }

    // Rectángulo (en espacio de Canvas) donde hay que dibujar una ROI ya confirmada, para
    // redibujar el overlay cada vez que cambian los Items o el tamaño del Canvas.
    public Rect ToControlRect(RoiDraftItem item, Size canvasSize)
    {
        var (scale, offsetX, offsetY) = ComputeTransform(canvasSize);
        return new Rect(offsetX + item.X * scale, offsetY + item.Y * scale, item.W * scale, item.H * scale);
    }

    [RelayCommand]
    private void DeleteItem(RoiDraftItem? item)
    {
        if (item is not null)
            Items.Remove(item);
    }

    [RelayCommand]
    private void ClearAll() => Items.Clear();

    public void Confirm()
        => ConfirmedRois = Items.Select(i => new RegionOfInterest(i.RoiId, i.X, i.Y, i.W, i.H, (double)i.Scale)).ToList();

    public void BeginDrag() => _isDragging = true;

    public void EndDrag() => _isDragging = false;

    private Point ToFrameSpace(Point p, Size canvasSize)
    {
        var (scale, offsetX, offsetY) = ComputeTransform(canvasSize);

        if (scale <= 0)
            return default;

        var x = Math.Clamp((p.X - offsetX) / scale, 0, _frameWidth);
        var y = Math.Clamp((p.Y - offsetY) / scale, 0, _frameHeight);
        return new Point(x, y);
    }

    // Stretch="Uniform" deja franjas (letterboxing) si la relación de aspecto del Canvas no
    // coincide con la del frame; scale/offset son los mismos para mapear en ambas direcciones.
    private (double scale, double offsetX, double offsetY) ComputeTransform(Size canvasSize)
    {
        if (_frameWidth <= 0 || _frameHeight <= 0 || canvasSize.Width <= 0 || canvasSize.Height <= 0)
            return (0, 0, 0);

        var scale = Math.Min(canvasSize.Width / _frameWidth, canvasSize.Height / _frameHeight);
        var offsetX = (canvasSize.Width - _frameWidth * scale) / 2;
        var offsetY = (canvasSize.Height - _frameHeight * scale) / 2;
        return (scale, offsetX, offsetY);
    }

    private void UpdateCounter() => CounterText = $"{Items.Count} / {_maxRois} ROIs";

    private async Task PullFrameAsync()
    {
        // Guarda de reentrancia: si un pull tarda más que el intervalo del timer, el siguiente
        // Tick se salta en vez de apilarse (GrabFrameAsync ya serializa por su cuenta, pero no
        // tiene sentido encolar pulls que van a llegar tarde de todas formas).
        if (_isPulling || _isDragging)
            return;

        _isPulling = true;
        try
        {
            var frame = await _frameCapture.GrabFrameAsync();
            PullCount++;

            // LinuxFrameCapture devuelve la MISMA instancia cuando GStreamer no tenía ningún
            // sample nuevo: nos ahorramos crear+copiar un WriteableBitmap entero (~15MB a esta
            // resolución) para volver a mostrar exactamente lo mismo que ya está en pantalla.
            if (ReferenceEquals(frame, _lastAppliedFrame))
                return;

            _lastAppliedFrame = frame;
            _frameWidth = frame.Width;
            _frameHeight = frame.Height;

            // Se crea un WriteableBitmap NUEVO por frame en vez de mutar uno existente in-place:
            // Avalonia (confirmado por sus propios mantenedores, ver AvaloniaUI/Avalonia#9835) no
            // detecta de forma confiable que los píxeles de un WriteableBitmap ya asignado a
            // Image.Source cambiaron -- InvalidateVisual() debería alcanzar según ellos, pero en
            // la práctica no siempre repinta. Reasignar la referencia sí dispara el binding normal
            // de Image.Source todas las veces.
            var bitmap = CreateBitmap(frame);
            WriteInto(bitmap, frame);

            var previous = PreviewBitmap;
            PreviewBitmap = bitmap;
            previous?.Dispose();

            NewFrameCount++;
        }
        catch (Exception ex)
        {
            // Un pull fallido no debe tumbar el preview: el próximo tick reintenta solo.
            StatusMessage = $"Preview interrumpido: {ex.Message}";
        }
        finally
        {
            _isPulling = false;
        }
    }

    private static WriteableBitmap CreateBitmap(Frame frame)
        => new(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

    private static void WriteInto(WriteableBitmap bitmap, Frame frame)
    {
        if (!MemoryMarshal.TryGetArray(frame.PixelData, out var segment) || segment.Array is null)
            throw new InvalidOperationException("Frame.PixelData no está respaldado por un array administrado.");

        using var fb = bitmap.Lock();
        var rowBytes = frame.Width * 4;

        if (fb.RowBytes == rowBytes)
        {
            Marshal.Copy(segment.Array, segment.Offset, fb.Address, rowBytes * frame.Height);
            return;
        }

        for (var row = 0; row < frame.Height; row++)
            Marshal.Copy(segment.Array, segment.Offset + row * rowBytes, fb.Address + row * fb.RowBytes, rowBytes);
    }

    public void Dispose() => _previewTimer.Stop();
}
