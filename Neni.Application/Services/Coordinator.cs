using Microsoft.Extensions.Logging;
using Neni.Abstractions.Interfaces;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Application.Interfaces;
using Neni.Application.Pipeline;

namespace Neni.Application.Services;

// Fachada de la pipeline para la UI: carga de motores, seleccion de ventana, ROIs y arranque/parada
// del ciclo. El trabajo de cada vuelta vive en Pipeline/ (CycleRunner, RoiProcessor, OverlayTracker,
// TranslationCache).
internal sealed class Coordinator : ICoordinator
{
    private readonly IPipelineEngines _engines;
    private readonly CycleRunner _cycleRunner;
    private readonly IFrameCapture _frameCapture;
    private readonly ICaptureTargetSelector _targetSelector;
    private readonly ILogger<Coordinator> _logger;
    private bool _disposed;
    // Unica fuente de verdad de las ROIs. Lista inmutable que se reemplaza entera: la UI la cambia
    // desde su hilo mientras el ciclo la lee desde el pool, y un swap de referencia es atomico.
    private IReadOnlyList<RegionOfInterest> _rois = [];
    private readonly Lock _roisGate = new();

    public Coordinator(
        IPipelineEngines engines,
        CycleRunner cycleRunner,
        IFrameCapture frameCapture,
        ICaptureTargetSelector targetSelector,
        ILogger<Coordinator> logger)
    {
        _logger = logger;
        _engines = engines;
        _cycleRunner = cycleRunner;
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

    // Captura un único frame de la ventana ya vinculada, sin pasar por deduplicación/OCR: es la
    // imagen sobre la que el usuario dibuja las ROIs.
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
    public Task StartCycle(IProgress<IReadOnlyDictionary<int, RoiReport>>? progress = null)
    {
        // Los motores solo quedan listos después de InitializeAsync(); fallar aqui da un mensaje
        // claro en vez de un error a mitad de la primera vuelta.
        if (!_engines.IsReady)
            throw new InvalidOperationException("InitializeAsync() debe ser invocado (y esperado) antes de iniciar el ciclo.");

        return _cycleRunner.RunAsync(() => RegionsOfInterest, progress);
    }

    /// <summary>Detiene la pipeline, espera a que el ciclo termine y limpia el estado de la sesion.</summary>
    public Task StopCycle() => _cycleRunner.StopAsync();

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
            _logger.LogError(ex, "Error deteniendo el ciclo durante el cierre");
        }
    }
}
