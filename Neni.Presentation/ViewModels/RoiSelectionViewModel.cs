using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Neni.Abstractions.Entities;

namespace Neni.Presentation.ViewModels;

// Muestra un único frame (el que ya trajo GrabFrameAsync antes de abrir esta ventana) y maneja el
// estado de las ROIs mientras RoiSelectionWindow está abierta. La conversión punto-de-pantalla <->
// píxel-de-frame vive acá para que el code-behind de la ventana (que solo conoce gestos de puntero
// y el árbol visual) no tenga que saber nada de Stretch="Uniform"/letterboxing.
public partial class RoiSelectionViewModel : ViewModelBase
{
    private const double MinDragSizePx = 4; // Por debajo de esto se trata como un clic accidental, no un arrastre.

    private readonly int _maxRois;
    private int _nextDraftId;
    private readonly int _frameWidth;
    private readonly int _frameHeight;

    [ObservableProperty]
    public partial WriteableBitmap? PreviewBitmap { get; set; }

    [ObservableProperty]
    public partial string CounterText { get; set; } = "";

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public ObservableCollection<RoiDraftItem> Items { get; } = new();

    // null hasta que se presiona "Confirmar" — RegionOfInterest (el servicio) lo lee después de
    // que la ventana cierra para distinguir "confirmó" de "cerró con la X sin confirmar".
    public IReadOnlyList<RegionOfInterest>? ConfirmedRois { get; private set; }

    public RoiSelectionViewModel(Frame frame, int maxRois, IEnumerable<RegionOfInterest> existingRois)
    {
        _maxRois = maxRois;
        _frameWidth = frame.Width;
        _frameHeight = frame.Height;

        foreach (var roi in existingRois)
            Items.Add(new RoiDraftItem(roi.RoiId, roi.X, roi.Y, roi.W, roi.H));

        _nextDraftId = Items.Count == 0 ? 1 : Items.Max(i => i.RoiId) + 1;
        Items.CollectionChanged += (_, _) => UpdateCounter();
        UpdateCounter();

        PreviewBitmap = CreateBitmap(frame);
        WriteInto(PreviewBitmap, frame);
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

        Items.Add(new RoiDraftItem(_nextDraftId++, x, y, w, h));
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
        => ConfirmedRois = Items.Select(i => new RegionOfInterest(i.RoiId, i.X, i.Y, i.W, i.H)).ToList();

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
}
