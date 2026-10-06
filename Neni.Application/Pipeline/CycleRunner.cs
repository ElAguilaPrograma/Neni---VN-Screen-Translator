using System.Diagnostics;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;

namespace Neni.Application.Pipeline;

// Bucle de la pipeline: una vuelta cada Settings.TimerCycleInterval ms hasta que se detenga. Es el
// unico hilo que toca el estado por ROI (RoiProcessor, OverlayTracker y los textos de aqui); la
// unica entrada que cruza hilos es la lista de ROIs, que llega como snapshot inmutable.
internal sealed class CycleRunner
{
    private readonly Settings _settings;
    private readonly IFrameCapture _frameCapture;
    private readonly IOverlay _overlay;
    private readonly RoiProcessor _roiProcessor;
    private readonly OverlayTracker _overlayTracker;
    private readonly Stopwatch _stopwatch = new();
    private readonly Dictionary<int, string> _texts = new();
    private bool _running;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private Frame? _lastWindowFrame;
    // ROIs que proceso la vuelta anterior: detecta cambios para purgar el estado que dejaron.
    private IReadOnlyList<RegionOfInterest>? _lastProcessedRois;

    public CycleRunner(
        Settings settings,
        IFrameCapture frameCapture,
        IOverlay overlay,
        RoiProcessor roiProcessor,
        OverlayTracker overlayTracker)
    {
        _settings = settings;
        _frameCapture = frameCapture;
        _overlay = overlay;
        _roiProcessor = roiProcessor;
        _overlayTracker = overlayTracker;
    }

    /// <summary>
    /// Corre el bucle hasta StopAsync. roiSource se consulta al inicio de cada vuelta, asi las ROIs
    /// pueden cambiar con el ciclo corriendo. Cada vuelta reporta el texto de cada ROI.
    /// </summary>
    public async Task RunAsync(
        Func<IReadOnlyList<RegionOfInterest>> roiSource,
        IProgress<IReadOnlyDictionary<int, string>>? progress)
    {
        var interval = _settings.TimerCycleInterval;

        if (interval <= 0)
            throw new InvalidOperationException("Settings.TimerCycleInterval debe ser mayor a 0.");

        if (_running)
            throw new InvalidOperationException("El ciclo ya esta corriendo.");

        _running = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            await _overlay.InitializeAsync();

            // Se guarda la tarea del bucle para que StopAsync pueda esperar a que termine de verdad.
            _loopTask = LoopAsync(roiSource, progress, interval, token);
            await _loopTask;
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            // Si el ciclo murio solo, se limpia aqui para que se pueda volver a arrancar.
            await EndSessionAsync();
            throw;
        }
    }

    /// <summary>Detiene el bucle, espera a que termine la vuelta en curso y limpia el estado de la sesion.</summary>
    public async Task StopAsync()
    {
        if (!_running)
            return;

        _cts?.Cancel();

        // Hay que esperar al bucle antes de soltar nada: si no, se puede limpiar el estado con un
        // GrabFrameAsync o un OCR todavia en vuelo.
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await EndSessionAsync();
    }

    /// <summary>
    /// Una vuelta sobre las ROIs dadas. Devuelve el texto actual de cada ROI (vacio si no hay ROIs).
    /// </summary>
    internal async Task<IReadOnlyDictionary<int, string>> ProcessTurnAsync(
        IReadOnlyList<RegionOfInterest> activeRois,
        CancellationToken cancellationToken)
    {
        var roisChanged = _lastProcessedRois is null || !_lastProcessedRois.SequenceEqual(activeRois);

        if (roisChanged)
        {
            PurgeStaleRoiState(activeRois);
            _lastProcessedRois = activeRois;
        }

        if (activeRois.Count == 0)
            return Snapshot();

        var windowFrame = await _frameCapture.GrabFrameAsync(cancellationToken);

        // IFrameCapture devuelve la misma instancia mientras no llegue un frame nuevo: si ademas las
        // ROIs no cambiaron, no hay nada que procesar en toda la vuelta.
        if (!roisChanged && ReferenceEquals(windowFrame, _lastWindowFrame))
            return Snapshot();

        _lastWindowFrame = windowFrame;

        foreach (var roi in activeRois)
        {
            var result = await _roiProcessor.ProcessAsync(roi, windowFrame, cancellationToken);

            // Unchanged o Failed: se conserva el texto y el overlay que ya tenia esta ROI.
            if (result.Outcome != RoiOutcome.Updated)
                continue;

            await _overlayTracker.ApplyAsync(roi, result.Blocks);
            _texts[roi.RoiId] = string.Join(Environment.NewLine, result.Blocks.Select(b => b.TranslatedText));
        }

        return Snapshot();
    }

    private async Task LoopAsync(
        Func<IReadOnlyList<RegionOfInterest>> roiSource,
        IProgress<IReadOnlyDictionary<int, string>>? progress,
        int interval,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _stopwatch.Restart();

                // Se lee una sola vez: toda la vuelta trabaja sobre la misma lista aunque la UI la cambie.
                var texts = await ProcessTurnAsync(roiSource(), cancellationToken);

                // Se reporta cada vuelta, cambie o no el texto: es el unico latido que tiene la UI para
                // distinguir "pantalla estatica" de "el ciclo se congelo".
                progress?.Report(texts);

                var remaining = interval - _stopwatch.ElapsedMilliseconds;

                if (remaining > 0)
                    await Task.Delay((int)remaining, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Suelta el estado de cada ROI de la vuelta anterior que ya no esta, o que cambio de geometria
    /// (mismo id, otro rectangulo: su firma y su texto ya no corresponden).
    /// </summary>
    private void PurgeStaleRoiState(IReadOnlyList<RegionOfInterest> currentRois)
    {
        if (_lastProcessedRois is null)
            return;

        foreach (var staleRoi in _lastProcessedRois.Where(roi => !currentRois.Contains(roi)))
        {
            _overlayTracker.Forget(staleRoi.RoiId);
            _roiProcessor.Forget(staleRoi.RoiId);
            _texts.Remove(staleRoi.RoiId);
        }
    }

    private async Task EndSessionAsync()
    {
        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
        _stopwatch.Stop();

        await _overlay.StopAsync();
        _overlayTracker.Reset();
        _roiProcessor.Reset();
        _lastWindowFrame = null;
        _lastProcessedRois = null;
        _texts.Clear();
        _running = false;
    }

    /// <summary>Copia el texto actual de cada ROI, para no exponer el diccionario mutable interno.</summary>
    private IReadOnlyDictionary<int, string> Snapshot() => new Dictionary<int, string>(_texts);
}
