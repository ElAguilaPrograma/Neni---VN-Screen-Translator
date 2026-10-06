using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;
using RapidOcrNet;

namespace Neni.Ocr.Services;

// Adaptador entre IOcrEngineFactory (Abstractions) y el factory estatico Ocr.CreateAsync.
// Mantiene los tipos especificos de RapidOcr (modelManager, options) fuera de Application.
internal sealed class RapidOcrEngineFactory : IOcrEngineFactory
{
    private readonly OcrSettings _settings;
    private readonly RapidOcrModelManagerService _modelManager;
    private readonly RapidOcrOptions? _options;
    private readonly ILogger _logger;

    public RapidOcrEngineFactory(
        OcrSettings settings,
        RapidOcrModelManagerService? modelManager = null,
        RapidOcrOptions? options = null,
        ILogger<Ocr>? logger = null)
    {
        _logger = logger ?? NullLogger<Ocr>.Instance;
        _settings = settings;
        _modelManager = modelManager ?? new RapidOcrModelManagerService();
        _options = options;
    }

    public async Task<IOcr> CreateAsync(CancellationToken cancellationToken = default)
    {
        return await Ocr.CreateAsync(_modelManager, _settings, _options, _logger, cancellationToken);
    }
}
