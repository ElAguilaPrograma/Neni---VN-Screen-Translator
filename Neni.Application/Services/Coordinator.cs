using Neni.Abstractions.Interfaces;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Application.Interfaces;
using Neni.Application.DataTransferObjets;
using System.Diagnostics;

namespace Neni.Application.Services;

public class Coordinator : ICoordinator
{
    private readonly IInitialize _initialize;
    private readonly IDeduplication _deduplication;
    private readonly IRegionOfInterest _regionOfInterest;
    private readonly IOverlay _overlay;
    private readonly IFrameCapture _frameCapture;
    private readonly IFrameProcessor _frameProcessor;
    private readonly ICaptureTargetSelector _targetSelector;
    private readonly Stopwatch stopwatch = new Stopwatch();
    private bool _isActive = false;
    private bool _overlaySession = false;
    private bool _disposed = false;
    // Firma del ultimo frame DESPACHADO a OCR por ROI (no la del ciclo anterior): solo se escribe
    // cuando la deduplicacion deja pasar el frame. Ver la nota en IDeduplication.IsDuplicate.
    private Dictionary<int, FrameSignature> _lastSignatures = new Dictionary<int, FrameSignature>();
    private Frame? _lastWindowFrame;
    private List<RegionOfInterest>? _lastProcessedRois;
    private Dictionary<int, string> _translationTexts = new Dictionary<int, string>();
    private Dictionary<string, string> _translationCache = new Dictionary<string, string>();
    // Ids de overlay actualmente en pantalla, por RoiId. Un ROI puede generar varios
    // items de overlay (uno por bloque de texto detectado por el OCR).
    private Dictionary<int, HashSet<int>> _activeOverlayItemIdsByRoi = new Dictionary<int, HashSet<int>>();

    public Coordinator(IInitialize initialize,
        IDeduplication deduplication,
        IRegionOfInterest regionOfInterest,
        IOverlay overlay,
        IFrameProcessor frameProcessor,
        IFrameCapture frameCapture,
        ICaptureTargetSelector targetSelector)
    {
        _initialize = initialize;
        _deduplication = deduplication;
        _regionOfInterest = regionOfInterest;
        _overlay = overlay;
        _frameProcessor = frameProcessor;
        _frameCapture = frameCapture;
        _targetSelector = targetSelector;
    }

    /// <summary>
    /// Carga por unica vez lo pesado (settings, motor de OCR, traductor). Es idempotente:
    /// re-inicializar dejaria sin liberar la sesion nativa del motor anterior.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialize.AppSettings is not null)
            return;

        await _initialize.InitializeAsync(cancellationToken);
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

    // Obtenemos las regiones de interés activas para la ventana objetivo seleccionada por el usuario
    public async Task<IEnumerable<RegionOfInterest>> GetRegionOfInterestAsync(Frame frame, CancellationToken cancellationToken = default)
        => await _regionOfInterest.DrawRoisAsync(frame, cancellationToken);

    // Eliminamos una región de interés específica identificada por su ID, junto con su overlay
    // y el estado en caché (último frame, última traducción) que quedó asociado a esa ROI.
    public void DeleteRegionOfInterest(int roiId)
    {
        _regionOfInterest.DeleteRoi(roiId);

        if (_activeOverlayItemIdsByRoi.TryGetValue(roiId, out var overlayItemIds))
        {
            foreach (var overlayItemId in overlayItemIds)
                _overlay.RemoveOverlayContent(overlayItemId);

            _activeOverlayItemIdsByRoi.Remove(roiId);
        }

        _lastSignatures.Remove(roiId);
        _translationTexts.Remove(roiId);
    }

    // Inicia la ejecución la pipeline
    public async Task StartCycle(IEnumerable<RegionOfInterestDto>? activeRoisDto = null)
    {
        EnsureInitialized();

        var interval = _initialize.AppSettings.TimerCycleInterval;

        if  (interval <= 0)
            throw new ArgumentOutOfRangeException(nameof(interval), "El intervalo de tiempo debe ser mayor a 0.");

        this._isActive = true;
        await _overlay.InitializeAsync();
        this._overlaySession = true;

        // Se ejecuta indefinidamente (hasta StopCycle) procesando un ciclo aprox. cada "interval" ms,
        // descontando el tiempo que el propio ProcessCycle tarda en correr.
        while (this._isActive)
        {
            stopwatch.Restart();
            await this.ProcessCycle(activeRoisDto);

            var remaining = interval - stopwatch.ElapsedMilliseconds;
            if (remaining > 0 && this._isActive)
                await Task.Delay((int)remaining);
        }
    }

    // Detiene la ejecución de la pipeline
    public async Task StopCycle()
    {
        if (!this._isActive)
            return;

        stopwatch.Stop();
        this._isActive = false;
        this._overlaySession = false;
        await _overlay.StopAsync();
        this._overlaySession = false;
        _activeOverlayItemIdsByRoi.Clear();
        _regionOfInterest.ClearRois();
        _lastWindowFrame = null;
        _lastProcessedRois = null;
        _lastSignatures.Clear();
        _translationTexts.Clear();
    }

    /// <summary>
    /// Detiene el ciclo y suelta todo lo que la pipeline sostiene vivo. La captura se libera antes
    /// que el selector porque la tuberia depende del nodo que vive en la sesion del portal.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await StopCycle();
        await _initialize.DisposeAsync();
        await _frameCapture.DisposeAsync();
        await _overlay.DisposeAsync();
        await _targetSelector.DisposeAsync();
    }

    // Ejecuta un ciclo de captura y procesamiento de frames, si la pipeline está activa y la sesión de overlay está activa.
    // Devuelve un diccionario con los textos traducidos para cada ROI procesada, 
    // o null si no hay ROIs activas o si la pipeline no está activa.
    public async Task<Dictionary<int, string>?> ProcessCycle(IEnumerable<RegionOfInterestDto>? activeRoisDto = null)
    {
        EnsureInitialized();

        var activeRois = activeRoisDto?.Select(MapRegionOfInterestDtoToRegionOfInterest).ToList();

        if (!this._isActive || !this._overlaySession)
        {
            Console.WriteLine("Pipeline is not active or overlay session is not active.");
            await this.StopCycle();
            return null;
        }

        if (activeRois == null || activeRois.Count == 0)
        {
            Console.WriteLine("No active ROIs provided. Skipping cycle processing.");
            return null;
        }

        Frame windowFrame = await _frameCapture.GrabFrameAsync();

        // Si la instancia del frame es la misma que la del ciclo anterior y las ROIs activas no cambiaron, 
        // no hay nada que procesar: devolvemos el resultado del ciclo anterior.
        if (ReferenceEquals(windowFrame, _lastWindowFrame)
            && _lastProcessedRois is not null
            && _lastProcessedRois.SequenceEqual(activeRois))
            return _translationTexts;

        _lastWindowFrame = windowFrame;
        _lastProcessedRois = activeRois;

        foreach (var roi in activeRois)
        {
            var ocrResult = await this.CaptureAndDispatch(roi, windowFrame);
            if (ocrResult == null)
            {
                // Sin cambio de frame para esta ROI (o error): no tocamos su overlay existente.
                continue;
            }

            var overlayCapibility = _overlay.CurrentOverlayCapability;
            var orderedBlocks = OrderBlocksReadingOrder(ocrResult.Blocks);
            var activeOverlayItemIds = _activeOverlayItemIdsByRoi.GetValueOrDefault(roi.RoiId);
            var currentOverlayItemIds = new HashSet<int>();
            var roiTranslatedLines = new List<string>();
            var newOverlayItems = new List<TranslationOverlayItem>();

            for (var blockIndex = 0; blockIndex < orderedBlocks.Count; blockIndex++)
            {
                var block = orderedBlocks[blockIndex];
                var ocrText = _initialize.Engine.NormalizeText(block.Text, _initialize.AppSettings.SourceLanguage);
                if (string.IsNullOrWhiteSpace(ocrText))
                    continue;

                if (!_translationCache.TryGetValue(ocrText, out var translatedText))
                {
                    translatedText = await _initialize.Translator.TranslateAsync(
                        ocrText,
                        _initialize.AppSettings.SourceLanguage,
                        _initialize.AppSettings.TargetLanguage);
                    _translationCache[ocrText] = translatedText;
                }

                roiTranslatedLines.Add(translatedText);

                if (overlayCapibility == OverlayCapability.CompanionWindowOnly)
                {
                    // Sin overlay directo disponible: el texto traducido ya quedó acumulado arriba.
                    continue;
                }

                IReadOnlyList<TextPoint> boxPoints = block.BoxPoints;
                var overlayItemId = MakeOverlayItemId(roi.RoiId, blockIndex);
                currentOverlayItemIds.Add(overlayItemId);

                var overlayItem = new TranslationOverlayItem(
                    overlayItemId,
                    ocrText,
                    translatedText,
                    new WindowBounds(
                        (int)boxPoints[0].X,
                        (int)boxPoints[0].Y,
                        (int)(boxPoints[2].X - boxPoints[0].X),
                        (int)(boxPoints[2].Y - boxPoints[0].Y)));

                if (activeOverlayItemIds != null && activeOverlayItemIds.Contains(overlayItemId))
                {
                    // Ya existe un overlay para este bloque: actualizamos su contenido.
                    _overlay.UpdateOverlayContent(overlayItem);
                }
                else
                {
                    newOverlayItems.Add(overlayItem);
                }
            }

            if (newOverlayItems.Count > 0)
                await _overlay.RenderTranslationOverlayAsync(newOverlayItems);

            // Bloques que estaban en pantalla el ciclo anterior para esta ROI y ya no aparecieron
            // (opción de menú cerrada, texto acortado, etc.): eliminamos su overlay.
            if (activeOverlayItemIds != null)
            {
                foreach (var staleId in activeOverlayItemIds.Except(currentOverlayItemIds))
                    _overlay.RemoveOverlayContent(staleId);
            }

            _activeOverlayItemIdsByRoi[roi.RoiId] = currentOverlayItemIds;
            _translationTexts[roi.RoiId] = string.Join(Environment.NewLine, roiTranslatedLines);
        }
        return _translationTexts;
    }

    // _initialize.AppSettings/Engine/Translator solo quedan listos después de InitializeAsync();
    // si alguien llama StartCycle/ProcessCycle antes de eso, fallamos con un mensaje claro en vez de un NRE opaco.
    private void EnsureInitialized()
    {
        if (_initialize.AppSettings is null)
            throw new InvalidOperationException("Initialize.InitializeAsync() debe ser invocado (y esperado) antes de iniciar el ciclo.");
    }

    // Mover este mapper a una carpeta de Helpers o Utils, para que pueda ser reutilizado en otras partes del proyecto si es necesario.
    private static RegionOfInterest MapRegionOfInterestDtoToRegionOfInterest(RegionOfInterestDto regionOfInterestDto)
        => new(
            regionOfInterestDto.RoiId,
            regionOfInterestDto.X,
            regionOfInterestDto.Y,
            regionOfInterestDto.W,
            regionOfInterestDto.H);

    // Id determinístico y estable para el overlay de un bloque de texto dentro de una ROI, derivado
    // de RoiId y la posición del bloque (de arriba hacia abajo) en el resultado de OCR de este ciclo.
    // Asume RoiId pequeño (Settings.MaxPendingRois = 8) y como máximo unos pocos cientos de bloques por ROI.
    private static int MakeOverlayItemId(int roiId, int blockIndex)
        => roiId * 1000 + blockIndex;

    // Ordena los bloques de texto de arriba hacia abajo para que el índice de cada bloque
    // se mantenga estable entre ciclos y así pueda usarse en MakeOverlayItemId.
    private static IReadOnlyList<OcrTextBlock> OrderBlocksReadingOrder(IReadOnlyList<OcrTextBlock> blocks)
        => blocks.OrderBy(b => b.BoxPoints[0].Y).ThenBy(b => b.BoxPoints[0].X).ToList();

    // Captura el frame de la ventana objetivo, recorta la región de interés y la envía al motor OCR para su procesamiento.
    // Devuelve null si no hubo cambio de frame para esta ROI (deduplicación) o si ocurrió un error.
    private async Task<OcrResult?> CaptureAndDispatch(RegionOfInterest roi, Frame windowFrame, bool forceRun = false)
    {
        if (roi == null)
        {
            Console.WriteLine("No active ROIs provided. Skipping capture and dispatch.");
            return null;
        }

        if (windowFrame == null)
        {
            Console.WriteLine("Failed to capture window frame. Skipping dispatch.");
            return null;
        }

        try
        {
            var croppedFrame = _frameProcessor.CropFrame(windowFrame, roi);

            if (croppedFrame == null)
            {
                Console.WriteLine($"Failed to crop frame for ROI: {roi}. Skipping this ROI.");
                return null;
            }

            var processedFrame = _frameProcessor.ProcessFrame(croppedFrame);
            var signature = _deduplication.ComputeSignature(processedFrame);

            if(!forceRun && _deduplication.IsDuplicate(signature, _lastSignatures.GetValueOrDefault(roi.RoiId)))
            {
                Console.WriteLine($"Duplicate frame detected for ROI: {roi}. Skipping dispatch.");
                return null;
            }

            var ocrResult = await _initialize.Engine.DetectAsync(processedFrame);
            _lastSignatures[roi.RoiId] = signature;

            return ocrResult;
        }

        catch (Exception ex)
        {
            Console.WriteLine($"Error during OCR processing: {ex.Message}");
            return null;
        }
    }
}