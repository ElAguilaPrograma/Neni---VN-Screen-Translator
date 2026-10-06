using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;
using RapidOcrNet;

namespace Neni.Ocr.Services;

// Adaptador entre IOcrEngineFactory (Abstractions) y el factory estatico Ocr.CreateAsync.
// Mantiene los tipos especificos de RapidOcr (modelManager, options) fuera de Application.
internal sealed class RapidOcrEngineFactory : IOcrEngineFactory
{
    private readonly Settings _settings;
    private readonly RapidOcrModelManagerService _modelManager;
    private readonly RapidOcrOptions? _options;

    public RapidOcrEngineFactory(
        Settings settings,
        RapidOcrModelManagerService? modelManager = null,
        RapidOcrOptions? options = null)
    {
        _settings = settings;
        _modelManager = modelManager ?? new RapidOcrModelManagerService();
        _options = options;
    }

    public async Task<IOcr> CreateAsync(CancellationToken cancellationToken = default)
    {
        return await Ocr.CreateAsync(_modelManager, _settings, _settings.OcrModelSize, _options, cancellationToken);
    }
}
