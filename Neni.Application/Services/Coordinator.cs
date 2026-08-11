using Neni.Abstractions.Interfaces;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Application.Interfaces;
using Neni.Application.DataTransferObjets;

namespace Neni.Application.Services;

// TODO Implementar la interfaz
public class Coordinator : ICoordinator
{
    private readonly Initialize _initialize;
    private readonly IDeduplication _deduplication;
    private readonly Settings _settings;
    private readonly IWindowLocator _windowLocator;
    private readonly IRegionOfInterest _regionOfInterest;
    private readonly IOverlay _overlay;
    private readonly IFrameCapture _frameCapture;
    private readonly IFrameProcessor _frameProcessor;
    private OcrResult _ocrResult = new OcrResult([]);
    private List<RegionOfInterest> _activeRoisOverlay = new List<RegionOfInterest>();
    private bool _isActive = false;
    private bool _overlaySession = false;
    private Frame? _lastFrame = null;

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
        _settings = _initialize.Setting.Load();
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
    public async Task<RegionOfInterest> GetRegionOfInterestAsync(Frame frame, CancellationToken cancellationToken = default)
        => await _regionOfInterest.DrawRoisAsync(frame, cancellationToken);

    // Eliminamos una región de interés específica identificada por su ID
    // TODO: _activeRoisOverlay debe ser publico para que pueda ser accedido desde la capa de presentación y se pueda eliminar el overlay correspondiente a la ROI eliminada.
    public void DeleteRegionOfInterest(int roiId)
        => _regionOfInterest.DeleteRoi(roiId);

    // Inicia la ejecución la pipeline
    public async Task StartCycle(IntPtr targetWindowHandle, IEnumerable<RegionOfInterestDto>? activeRoisDto = null)
    {
        var interval = _settings.TimerCycleInterval;

        if  (interval <= 0)
            throw new ArgumentOutOfRangeException(nameof(interval), "El intervalo de tiempo debe ser mayor a 0.");

        this._isActive = true;
        this._overlaySession = true;
        await _overlay.InitializeAsync(targetWindowHandle);
        await this.ProcessCycle(activeRoisDto);
    }

    // Detiene la ejecución de la pipeline
    public async Task StopCycle()
    {
        if (!this._isActive)
            return;
        
        this._isActive = false;
        await _overlay.StopAsync();
        this._overlaySession = false;
        _activeRoisOverlay.Clear();
        _regionOfInterest.ClearRois();
    }

    // Ejecuta un ciclo de captura y procesamiento de frames, si la pipeline está activa y la sesión de overlay está activa.
    // Solo devuelve un string el modo esta en ventana acompañante, en caso contrario devuelve null pues el overlay ya dibujara el texto traducido. 
    // El string devuelto es el texto traducido.
    public async Task<string?> ProcessCycle(IEnumerable<RegionOfInterestDto>? activeRoisDto = null)
    {
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

        foreach (var roi in activeRois)
        {
            await this.CaptureAndDispatch(roi);
            if (_ocrResult == null || string.IsNullOrWhiteSpace(_ocrResult.FullText))
            {
                Console.WriteLine("No OCR output detected. Skipping normalization.");
                return null;
            }

            var ocrText = _initialize.Engine.NormalizeText(_ocrResult.FullText, _settings.SourceLanguage);

            string translatedText = await _initialize.Translator.TranslateAsync(ocrText, _settings.SourceLanguage, _settings.TargetLanguage);

            // TODO: Si en la ROI hay más de un bloque de texto, se debe de iterar sobre cada bloque y dibujar cada uno en el overlay, actualmente solo se dibuja el primer bloque.
            OcrTextBlock ocrTextBlock = _ocrResult.Blocks[0];
            IReadOnlyList<TextPoint> boxPoints = ocrTextBlock.BoxPoints;
            var overlayCapibility = _overlay.CurrentOverlayCapability;

            if (overlayCapibility == OverlayCapability.CompanionWindowOnly)
            {
                return translatedText;
            }

            if (_activeRoisOverlay.Contains(roi))
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
        return null;
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
    private async Task CaptureAndDispatch(RegionOfInterest roi, bool forceRun = false)
    {
        if (roi == null)
        {
            Console.WriteLine("No active ROIs provided. Skipping capture and dispatch.");
            return;
        }

        Frame windowFrame = await _frameCapture.GrabFrameAsync();
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

            if(!forceRun && _deduplication.IsDuplicate(processedFrame, _lastFrame))
            {
                Console.WriteLine($"Duplicate frame detected for ROI: {roi}. Skipping dispatch.");
                return;
            }

            _ocrResult = await _initialize.Engine.DetectAsync(processedFrame);
            _lastFrame = processedFrame;

            return;
        }

        catch (Exception ex)
        {
            Console.WriteLine($"Error during OCR processing: {ex.Message}");
            return;
        }
    }
}