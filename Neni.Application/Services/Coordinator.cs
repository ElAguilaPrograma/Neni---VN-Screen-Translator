using Neni.Abstractions.Interfaces;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Application.Interfaces;
using Neni.Application.DataTransferObjets;
using System.Diagnostics;

namespace Neni.Application.Services;

public class Coordinator : ICoordinator
{
    private readonly Initialize _initialize;
    private readonly IDeduplication _deduplication;
    private readonly IRegionOfInterest _regionOfInterest;
    private readonly IOverlay _overlay;
    private readonly IFrameCapture _frameCapture;
    private readonly IFrameProcessor _frameProcessor;
    private readonly ICaptureTargetSelector _targetSelector;
    private readonly Stopwatch stopwatch = new Stopwatch();
    private bool _isActive = false;
    private bool _overlaySession = false;
    private Dictionary<int, Frame> _lastFrames = new Dictionary<int, Frame>();
    private Dictionary<int, string> _translationTexts = new Dictionary<int, string>();
    private Dictionary<string, string> _translationCache = new Dictionary<string, string>();
    // Ids de overlay actualmente en pantalla, por RoiId. Un ROI puede generar varios
    // items de overlay (uno por bloque de texto detectado por el OCR).
    private Dictionary<int, HashSet<int>> _activeOverlayItemIdsByRoi = new Dictionary<int, HashSet<int>>();

    public Coordinator(Initialize initialize,
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

        _lastFrames.Remove(roiId);
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
        this._overlaySession = true;
        await _overlay.InitializeAsync();

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
            regionOfInterestDto.H,
            regionOfInterestDto.Scale);

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
            var croppedFrame = _frameProcessor.CropFrames(windowFrame, roi);

            if (croppedFrame == null)
            {
                Console.WriteLine($"Failed to crop frame for ROI: {roi}. Skipping this ROI.");
                return null;
            }

            var processedFrame = _frameProcessor.ProcessFrames(croppedFrame);

            if(!forceRun && _deduplication.IsDuplicate(processedFrame, _lastFrames.GetValueOrDefault(roi.RoiId)))
            {
                Console.WriteLine($"Duplicate frame detected for ROI: {roi}. Skipping dispatch.");
                return null;
            }

            var ocrResult = await _initialize.Engine.DetectAsync(processedFrame);
            _lastFrames[roi.RoiId] = processedFrame;

            return ocrResult;
        }

        catch (Exception ex)
        {
            Console.WriteLine($"Error during OCR processing: {ex.Message}");
            return null;
        }
    }
}