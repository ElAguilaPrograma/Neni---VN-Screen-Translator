using Microsoft.Extensions.Logging;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;
using Neni.Application.Interfaces;

namespace Neni.Application.Pipeline;

// El nucleo de la pipeline para una ROI y un frame: recorte -> preprocesado -> deduplicacion ->
// OCR -> normalizacion -> traduccion. Solo lo usa el hilo del ciclo.
internal sealed class RoiProcessor
{
    private readonly IFrameProcessor _frameProcessor;
    private readonly IDeduplication _deduplication;
    private readonly IPipelineEngines _engines;
    private readonly ITextNormalizer _textNormalizer;
    private readonly TranslationCache _translationCache;
    private readonly TranslationSettings _settings;
    private readonly ILogger<RoiProcessor> _logger;
    // Firma del ultimo frame DESPACHADO a OCR por ROI (no la de la vuelta anterior): solo se escribe
    // cuando la ROI se proceso completa. Ver la nota en IDeduplication.IsDuplicate.
    private readonly Dictionary<int, FrameSignature> _lastSignatures = new();

    public RoiProcessor(
        IFrameProcessor frameProcessor,
        IDeduplication deduplication,
        IPipelineEngines engines,
        ITextNormalizer textNormalizer,
        TranslationCache translationCache,
        TranslationSettings settings,
        ILogger<RoiProcessor> logger)
    {
        _logger = logger;
        _frameProcessor = frameProcessor;
        _deduplication = deduplication;
        _engines = engines;
        _textNormalizer = textNormalizer;
        _translationCache = translationCache;
        _settings = settings;
    }

    /// <summary>
    /// Procesa una ROI del frame. Unchanged si la deduplicacion la descarta, Updated con los bloques
    /// traducidos en orden de lectura, o Failed si algo fallo (la firma no se guarda, asi la siguiente
    /// vuelta reintenta en vez de darla por vista).
    /// </summary>
    public async Task<RoiResult> ProcessAsync(RegionOfInterest roi, Frame windowFrame, CancellationToken cancellationToken = default)
    {
        try
        {
            var croppedFrame = _frameProcessor.CropFrame(windowFrame, roi);
            var processedFrame = _frameProcessor.ProcessFrame(croppedFrame);
            var signature = _deduplication.ComputeSignature(processedFrame);

            if (_deduplication.IsDuplicate(signature, _lastSignatures.GetValueOrDefault(roi.RoiId)))
                return RoiResult.Unchanged;

            var ocrResult = await _engines.Ocr.DetectAsync(processedFrame, cancellationToken);
            var blocks = await TranslateBlocksAsync(ocrResult, cancellationToken);

            // Al final a proposito: si el OCR o la traduccion fallan, esta firma no cuenta como despachada.
            _lastSignatures[roi.RoiId] = signature;

            return RoiResult.Updated(blocks);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error procesando la ROI {RoiId}", roi.RoiId);
            return RoiResult.Failed(ex.Message);
        }
    }

    /// <summary>Olvida la firma de una ROI: la proxima vez se procesa como nueva.</summary>
    public void Forget(int roiId) => _lastSignatures.Remove(roiId);

    /// <summary>Olvida todas las firmas; para cuando se detiene el ciclo.</summary>
    public void Reset() => _lastSignatures.Clear();

    private async Task<IReadOnlyList<TranslatedBlock>> TranslateBlocksAsync(OcrResult ocrResult, CancellationToken cancellationToken)
    {
        var orderedBlocks = OrderBlocksReadingOrder(ocrResult.Blocks);
        var translatedBlocks = new List<TranslatedBlock>();

        for (var blockIndex = 0; blockIndex < orderedBlocks.Count; blockIndex++)
        {
            var block = orderedBlocks[blockIndex];
            var text = _textNormalizer.Normalize(block.Text, _settings.SourceLanguage);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var translated = await _translationCache.TranslateAsync(text, cancellationToken);
            translatedBlocks.Add(new TranslatedBlock(blockIndex, text, translated, block.BoxPoints));
        }

        return translatedBlocks;
    }

    // Ordena los bloques de arriba hacia abajo para que el indice de cada uno se mantenga estable
    // entre vueltas y el overlay pueda reconocerlo.
    private static IReadOnlyList<OcrTextBlock> OrderBlocksReadingOrder(IReadOnlyList<OcrTextBlock> blocks)
        => blocks.OrderBy(b => b.BoxPoints[0].Y).ThenBy(b => b.BoxPoints[0].X).ToList();
}
