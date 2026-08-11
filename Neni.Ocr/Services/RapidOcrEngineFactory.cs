using Neni.Abstractions.Interfaces;
using RapidOcrNet;

namespace Neni.Ocr.Services;

// Adaptador entre IOcrEngineFactory (Abstractions) y el factory estatico Ocr.CreateAsync.
// Mantiene los tipos especificos de RapidOcr (modelManager, version, options) fuera de Application.
public sealed class RapidOcrEngineFactory : IOcrEngineFactory
{
    private readonly RapidOcrModelManagerService _modelManager;
    private readonly RapidOcrVersion _version;
    private readonly RapidOcrOptions? _options;

    public RapidOcrEngineFactory(
        RapidOcrModelManagerService? modelManager = null,
        RapidOcrVersion version = RapidOcrVersion.V5,
        RapidOcrOptions? options = null)
    {
        _modelManager = modelManager ?? new RapidOcrModelManagerService();
        _version = version;
        _options = options;
    }

    public async Task<IOcr> CreateAsync(CancellationToken cancellationToken = default) =>
        await Ocr.CreateAsync(_modelManager, _version, _options, cancellationToken);
}
