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
    private readonly IWindowLocator _windowLocator;
    private readonly IRegionOfInterest _regionOfInterest;
    private readonly IOverlay _overlay;
    private readonly IFrameCapture _frameCapture;
    private readonly IFrameProcessor _frameProcessor;
    private readonly Stopwatch stopwatch = new Stopwatch();
    private OcrResult _ocrResult = new OcrResult([]);
    private List<RegionOfInterest> _activeRoisOverlay = new List<RegionOfInterest>();
    private bool _isActive = false;
    private bool _overlaySession = false;
    private Dictionary<int, Frame> _lastFrames = new Dictionary<int, Frame>();
    private Dictionary<int, string> _translationTexts = new Dictionary<int, string>();
    private Dictionary<string, string> _translationCache = new Dictionary<string, string>();

    public Coordinator(Initialize initialize,
        IDeduplication deduplication,
        IWindowLocator windowLocator,
        IRegionOfInterest regionOfInterest,
        IOverlay overlay,
        IFrameProcessor frameProcessor,
        IFrameCapture frameCapture)
    {
        _initialize = initialize;
        _deduplication = deduplication;
        _windowLocator = windowLocator;
        _regionOfInterest = regionOfInterest;
        _overlay = overlay;
        _frameProcessor = frameProcessor;
        _frameCapture = frameCapture;
    }
    
    // Obtenemos la lista de ventanas disponibles para seleccionar la ventana objetivo
    public IEnumerable<WindowInfo> GetTargetWindow()
        => _windowLocator.ListAvailableWindows();

    // Obtenemos la información de la ventana objetivo seleccionada por el usuario
    public WindowInfo GetTargetWindowInfo(IntPtr handle)
        =>  _windowLocator.GetWindowInfo(handle);

    // Obtenemos las regiones de interés activas para la ventana objetivo seleccionada por el usuario
    public async Task<IEnumerable<RegionOfInterest>> GetRegionOfInterestAsync(Frame frame, CancellationToken cancellationToken = default)
        => await _regionOfInterest.DrawRoisAsync(frame, cancellationToken);

    // Eliminamos una región de interés específica identificada por su ID, junto con su overlay
    // y el estado en caché (último frame, última traducción) que quedó asociado a esa ROI.
    public void DeleteRegionOfInterest(int roiId)
    {
        _regionOfInterest.DeleteRoi(roiId);
        _overlay.RemoveOverlayContent(roiId);
        _activeRoisOverlay.RemoveAll(r => r.RoiId == roiId);
        _lastFrames.Remove(roiId);
        _translationTexts.Remove(roiId);
    }

    // Inicia la ejecución la pipeline
    public async Task StartCycle(IntPtr targetWindowHandle, IEnumerable<RegionOfInterestDto>? activeRoisDto = null)
    {
        EnsureInitialized();

        var interval = _initialize.AppSettings.TimerCycleInterval;

        if  (interval <= 0)
            throw new ArgumentOutOfRangeException(nameof(interval), "El intervalo de tiempo debe ser mayor a 0.");

        this._isActive = true;
        this._overlaySession = true;
        await _overlay.InitializeAsync(targetWindowHandle);

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
        _activeRoisOverlay.Clear();
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
        string translatedText = string.Empty;

        foreach (var roi in activeRois)
        {
            await this.CaptureAndDispatch(roi, windowFrame);
            if (_ocrResult == null || string.IsNullOrWhiteSpace(_ocrResult.FullText))
            {
                Console.WriteLine("No OCR output detected. Skipping normalization.");
                continue;
            }

            var ocrText = _initialize.Engine.NormalizeText(_ocrResult.FullText, _initialize.AppSettings.SourceLanguage);

            if (_translationCache.TryGetValue(ocrText, out string? cachedTranslation))
            {
                // Solo para quitar el warning, en teorio cachedTranslation nunca debería ser null, 
                // porque si está en el diccionario, tiene que tener un valor asociado.
                translatedText = cachedTranslation ?? string.Empty;

                if (_translationTexts.ContainsKey(roi.RoiId))
                {
                    _translationTexts[roi.RoiId] = translatedText;
                }
                else
                {
                    _translationTexts.Add(roi.RoiId, translatedText);
                }
            }
            else
            {
                translatedText = await _initialize.Translator.TranslateAsync(
                    ocrText, 
                    _initialize.AppSettings.SourceLanguage, 
                    _initialize.AppSettings.TargetLanguage);
                _translationCache[ocrText] = translatedText;

                if (_translationTexts.ContainsKey(roi.RoiId))
                {
                    _translationTexts[roi.RoiId] = translatedText;
                }
                else
                {
                    _translationTexts.Add(roi.RoiId, translatedText);
                }
            }

            // TODO: Si en la ROI hay más de un bloque de texto, se debe de iterar sobre cada bloque y dibujar cada uno en el overlay, actualmente solo se dibuja el primer bloque.
            OcrTextBlock ocrTextBlock = _ocrResult.Blocks[0];
            IReadOnlyList<TextPoint> boxPoints = ocrTextBlock.BoxPoints;
            var overlayCapibility = _overlay.CurrentOverlayCapability;

            if (overlayCapibility == OverlayCapability.CompanionWindowOnly)
            {
                // Sin overlay directo disponible: el texto traducido ya quedó en _translationTexts para esta ROI,
                // seguimos con el resto sin intentar dibujar overlay.
                continue;
            }

            if (_activeRoisOverlay.Any(r => r.RoiId == roi.RoiId))
            {
                // Si ya existe un overlay para esta ROI, actualizamos el contenido del overlay con el nuevo texto traducido y los bounds de la ventana objetivo
                var overlayItem = new TranslationOverlayItem(
                    roi.RoiId,
                    ocrText,
                    translatedText,
                    new WindowBounds(
                        (int)boxPoints[0].X,
                        (int)boxPoints[0].Y,
                        (int)(boxPoints[2].X - boxPoints[0].X),
                        (int)(boxPoints[2].Y - boxPoints[0].Y)));

                _overlay.UpdateOverlayContent(overlayItem);
            }
            else
            {
                // Crear un objeto TranslationOverlayItem con el texto original, el texto traducido y los bounds de la ventana objetivo
                var overlayItem = new List<TranslationOverlayItem>
                {
                    new TranslationOverlayItem(
                        roi.RoiId,
                        ocrText,
                        translatedText,
                        new WindowBounds(
                            (int)boxPoints[0].X,
                            (int)boxPoints[0].Y,
                            (int)(boxPoints[2].X - boxPoints[0].X),
                            (int)(boxPoints[2].Y - boxPoints[0].Y)))
                };

                await _overlay.RenderTranslationOverlayAsync(overlayItem);
                _activeRoisOverlay.Add(roi);
            }
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

    // Captura el frame de la ventana objetivo, recorta las regiones de interés activas y las envía al motor OCR para su procesamiento.
    private async Task CaptureAndDispatch(RegionOfInterest roi, Frame windowFrame, bool forceRun = false)
    {
        if (roi == null)
        {
            Console.WriteLine("No active ROIs provided. Skipping capture and dispatch.");
            return;
        }

        if (windowFrame == null)
        {
            Console.WriteLine("Failed to capture window frame. Skipping dispatch.");
            return;
        }

        try
        {
            var croppedFrame = _frameProcessor.CropFrames(windowFrame, roi);

            if (croppedFrame == null)
            {
                Console.WriteLine($"Failed to crop frame for ROI: {roi}. Skipping this ROI.");
                return;
            }

            var processedFrame = _frameProcessor.ProcessFrames(croppedFrame);

            if(!forceRun && _deduplication.IsDuplicate(processedFrame, _lastFrames.GetValueOrDefault(roi.RoiId)))
            {
                Console.WriteLine($"Duplicate frame detected for ROI: {roi}. Skipping dispatch.");
                return;
            }

            _ocrResult = await _initialize.Engine.DetectAsync(processedFrame);
            if (_lastFrames.ContainsKey(roi.RoiId))
            {
                _lastFrames[roi.RoiId] = processedFrame;
            }
            else
            {
                _lastFrames.Add(roi.RoiId, processedFrame);
            }

            return;
        }

        catch (Exception ex)
        {
            Console.WriteLine($"Error during OCR processing: {ex.Message}");
            return;
        }
    }
}