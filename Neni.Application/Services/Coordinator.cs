using Neni.Abstractions.Interfaces;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Application.Interfaces;
using Neni.Application.Pipeline;
using System.Diagnostics;

namespace Neni.Application.Services;

internal sealed class Coordinator : ICoordinator
{
    private readonly Settings _settings;
    private readonly IPipelineEngines _engines;
    private readonly IOverlay _overlay;
    private readonly IFrameCapture _frameCapture;
    private readonly ICaptureTargetSelector _targetSelector;
    private readonly Stopwatch stopwatch = new Stopwatch();
    private bool _isActive = false;
    private bool _overlaySession = false;
    private bool _disposed = false;
    // Unica fuente de verdad de las ROIs. Lista inmutable que se reemplaza entera: la UI la cambia
    // desde su hilo mientras el ciclo la lee desde el pool, y un swap de referencia es atomico.
    private IReadOnlyList<RegionOfInterest> _rois = [];
    private readonly Lock _roisGate = new();
    private Frame? _lastWindowFrame;
    // ROIs que proceso la vuelta anterior: detecta cambios para purgar el estado que dejaron.
    private IReadOnlyList<RegionOfInterest>? _lastProcessedRois;
    private CancellationTokenSource? _cycleCts;
    private Task? _cycleTask;
    private Dictionary<int, string> _translationTexts = new Dictionary<int, string>();
    private readonly RoiProcessor _roiProcessor;
    private readonly OverlayTracker _overlayTracker;

    public Coordinator(Settings settings,
        IPipelineEngines engines,
        RoiProcessor roiProcessor,
        OverlayTracker overlayTracker,
        IOverlay overlay,
        IFrameCapture frameCapture,
        ICaptureTargetSelector targetSelector)
    {
        _settings = settings;
        _engines = engines;
        _roiProcessor = roiProcessor;
        _overlayTracker = overlayTracker;
        _overlay = overlay;
        _frameCapture = frameCapture;
        _targetSelector = targetSelector;
    }

    /// <summary>
    /// Carga por unica vez lo pesado (motor de OCR, traductor). Es idempotente:
    /// re-inicializar dejaria sin liberar la sesion nativa del motor anterior.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_engines.IsReady)
            return;

        await _engines.InitializeAsync(cancellationToken);
    }

    // Llama al selector de ventanas para que el usuario elija la ventana objetivo a traducir.
    // Devuelve los candidatos: una lista para que la UI monte su propio selector donde se pueden
    // enumerar ventanas, o como mucho un elemento (vacía si se canceló) donde hay que pasar por el
    // diálogo nativo del sistema. Quién puede hacer qué lo dice el selector, no esta capa.
    public async Task<IEnumerable<CaptureTarget>> OpenWindowSelectorAsync(
        bool reuseLastSelection = false,
        CancellationToken cancellationToken = default)
    {
        if (_targetSelector.SelectionMode == TargetSelectionMode.Enumerable)
            return await _targetSelector.ListAvailableTargetsAsync(cancellationToken);

        var selectedTarget = await _targetSelector.PromptTargetSelectionAsync(reuseLastSelection, cancellationToken);

        return selectedTarget is null ? [] : [selectedTarget];
    }

    // Vincula la ventana elegida por el usuario a la sesión de captura, antes de arrancar el ciclo.
    public async Task AttachToTargetAsync(CaptureTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!target.IsValid)
            throw new ArgumentException(
                "El objetivo de captura no tiene ni handle nativo ni node id de PipeWire.", nameof(target));

        await _frameCapture.AttachToTargetAsync(target, cancellationToken);
    }

    // Captura un único frame de la ventana ya vinculada, sin pasar por deduplicación/OCR.
    // Sirve para verificar que la sesión de captura quedó operativa tras AttachToTargetAsync y,
    // más adelante, como la fuente de imagen sobre la que el usuario dibuja las ROIs.
    public async Task<Frame> GrabPreviewFrameAsync(CancellationToken cancellationToken = default)
        => await _frameCapture.GrabFrameAsync(cancellationToken);

    public IReadOnlyList<RegionOfInterest> RegionsOfInterest => Volatile.Read(ref _rois);

    /// <summary>
    /// Reemplaza las ROIs. Se puede llamar con el ciclo corriendo: la siguiente vuelta toma la lista
    /// nueva y purga el estado (firma, texto, overlay) de las ROIs que desaparecieron o cambiaron.
    /// </summary>
    public void SetRegionsOfInterest(IEnumerable<RegionOfInterest> rois)
    {
        ArgumentNullException.ThrowIfNull(rois);

        var snapshot = rois.ToArray();

        if (snapshot.Select(roi => roi.RoiId).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Hay ROIs con el RoiId repetido.", nameof(rois));

        lock (_roisGate)
            Volatile.Write(ref _rois, snapshot);
    }

    /// <summary>Quita una ROI; su estado en cache se purga en la siguiente vuelta del ciclo.</summary>
    public void DeleteRegionOfInterest(int roiId)
    {
        lock (_roisGate)
            Volatile.Write(ref _rois, _rois.Where(roi => roi.RoiId != roiId).ToArray());
    }

    /// <summary>
    /// Arranca la pipeline y la mantiene corriendo hasta StopCycle. No retorna mientras el ciclo
    /// siga vivo. En cada vuelta reporta por progress el texto actual de cada ROI, indexado por RoiId.
    /// </summary>
    public async Task StartCycle(IProgress<IReadOnlyDictionary<int, string>>? progress = null)
    {
        EnsureInitialized();

        var interval = _settings.TimerCycleInterval;

        if  (interval <= 0)
            throw new ArgumentOutOfRangeException(nameof(interval), "El intervalo de tiempo debe ser mayor a 0.");

        this._isActive = true;
        await _overlay.InitializeAsync();
        this._overlaySession = true;

        _cycleCts = new CancellationTokenSource();

        // Se guarda la tarea del bucle para que StopCycle pueda esperar a que termine de verdad.
        _cycleTask = RunCycleAsync(progress, interval, _cycleCts.Token);

        await _cycleTask;
    }

    /// <summary>
    /// Bucle de la pipeline: procesa una vuelta, reporta el resultado y espera lo que reste del
    /// intervalo, hasta que se cancele.
    /// </summary>
    private async Task RunCycleAsync(
        IProgress<IReadOnlyDictionary<int, string>>? progress,
        int interval,
        CancellationToken cancellationToken)
    {
        while (this._isActive)
        {
            stopwatch.Restart();

            var texts = await this.ProcessCycle();

            // Se reporta cada vuelta, cambie o no el texto: es el unico latido que tiene la UI para
            // distinguir "pantalla estatica" de "el ciclo se congelo".
            if (texts is not null)
                progress?.Report(texts);

            var remaining = interval - stopwatch.ElapsedMilliseconds;

            if (remaining <= 0)
                continue;

            try
            {
                await Task.Delay((int)remaining, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
    /// <summary>Detiene la pipeline, espera a que el ciclo termine y limpia el estado de la sesion.</summary>
    public async Task StopCycle()
    {
        if (!this._isActive)
            return;

        this._isActive = false;
        _cycleCts?.Cancel();

        // Hay que esperar al bucle antes de soltar nada: si no, se puede liberar la captura con un
        // GrabFrameAsync todavia en vuelo.
        if (_cycleTask is not null)
        {
            try
            {
                await _cycleTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cycleCts?.Dispose();
        _cycleCts = null;
        _cycleTask = null;

        stopwatch.Stop();
        this._overlaySession = false;
        await _overlay.StopAsync();
        _overlayTracker.Reset();
        _lastWindowFrame = null;
        _lastProcessedRois = null;
        _roiProcessor.Reset();
        _translationTexts.Clear();
    }

    /// <summary>
    /// Detiene el ciclo. No libera sus dependencias: no las creo, su dueño es el contenedor, que
    /// libera al Coordinator primero (es lo ultimo que crea) y despues a todo lo demas.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Un fallo del ciclo no puede impedir el cierre: quien espera StartCycle ya lo observa.
        try
        {
            await StopCycle();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deteniendo el ciclo durante el cierre: {ex.Message}");
        }
    }

    /// <summary>
    /// Ejecuta una sola vuelta de la pipeline sobre las ROIs actuales. Devuelve el texto de cada ROI
    /// indexado por RoiId (vacio si no hay ROIs), o null si la pipeline no esta activa.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, string>?> ProcessCycle()
    {
        EnsureInitialized();

        if (!this._isActive || !this._overlaySession)
        {
            Console.WriteLine("Pipeline is not active or overlay session is not active.");
            return null;
        }

        // Se lee una sola vez: toda la vuelta trabaja sobre la misma lista aunque la UI la cambie.
        var activeRois = RegionsOfInterest;
        var roisChanged = _lastProcessedRois is null || !_lastProcessedRois.SequenceEqual(activeRois);

        if (roisChanged)
        {
            PurgeStaleRoiState(activeRois);
            _lastProcessedRois = activeRois;
        }

        if (activeRois.Count == 0)
            return Snapshot();

        Frame windowFrame = await _frameCapture.GrabFrameAsync();

        // Si la instancia del frame es la misma que la del ciclo anterior y las ROIs activas no cambiaron,
        // no hay nada que procesar: devolvemos el resultado del ciclo anterior.
        if (!roisChanged && ReferenceEquals(windowFrame, _lastWindowFrame))
            return Snapshot();

        _lastWindowFrame = windowFrame;

        foreach (var roi in activeRois)
        {
            var result = await _roiProcessor.ProcessAsync(roi, windowFrame);

            // Unchanged o Failed: se conserva el texto y el overlay que ya tenia esta ROI.
            if (result.Outcome != RoiOutcome.Updated)
                continue;

            await _overlayTracker.ApplyAsync(roi, result.Blocks);
            _translationTexts[roi.RoiId] = string.Join(Environment.NewLine, result.Blocks.Select(b => b.TranslatedText));
        }
        return Snapshot();
    }

    /// <summary>
    /// Suelta el estado de cada ROI de la vuelta anterior que ya no esta, o que cambio de geometria
    /// (mismo id, otro rectangulo: su firma y su texto ya no corresponden). Corre en el hilo del ciclo,
    /// el unico que toca estos diccionarios.
    /// </summary>
    private void PurgeStaleRoiState(IReadOnlyList<RegionOfInterest> currentRois)
    {
        if (_lastProcessedRois is null)
            return;

        foreach (var staleRoi in _lastProcessedRois.Where(roi => !currentRois.Contains(roi)))
        {
            _overlayTracker.Forget(staleRoi.RoiId);
            _roiProcessor.Forget(staleRoi.RoiId);
            _translationTexts.Remove(staleRoi.RoiId);
        }
    }

    /// <summary>Copia el texto actual de cada ROI, para no exponer el diccionario mutable interno.</summary>
    private IReadOnlyDictionary<int, string> Snapshot() => new Dictionary<int, string>(_translationTexts);

    // Los motores solo quedan listos después de InitializeAsync();
    // si alguien llama StartCycle/ProcessCycle antes de eso, fallamos con un mensaje claro en vez de un NRE opaco.
    private void EnsureInitialized()
    {
        if (!_engines.IsReady)
            throw new InvalidOperationException("InitializeAsync() debe ser invocado (y esperado) antes de iniciar el ciclo.");
    }
}
