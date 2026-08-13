using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;
using Neni.Ocr.Services;

namespace Neni.Tests.Ocr.Helpers;

// Comparte una sola instancia de IOcrEngine entre todos los tests del collection,
// asi los modelos ONNX se cargan una vez en vez de una vez por caso de test.
public sealed class OcrEngineFixture : IAsyncLifetime
{
    public IOcr Engine { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var modelManager = new RapidOcrModelManagerService();
        Engine = await Neni.Ocr.Services.Ocr.CreateAsync(modelManager, new Settings(), OcrModelSize.Small);
    }

    public async Task DisposeAsync()
    {
        await Engine.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class OcrEngineCollection : ICollectionFixture<OcrEngineFixture>
{
    public const string Name = "OcrEngine";
}
