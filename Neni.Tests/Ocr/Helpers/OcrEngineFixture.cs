using Neni.Abstractions.Interfaces;
using Neni.Ocr.Services;

namespace Neni.Tests.Ocr.Helpers;

// Comparte una sola instancia de IOcrEngine entre todos los tests del collection,
// asi los modelos ONNX se cargan una vez en vez de una vez por caso de test.
public sealed class OcrEngineFixture : IAsyncLifetime
{
    public IOcrEngine Engine { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var modelManager = new RapidOcrModelManagerService();
        Engine = await OcrEngine.CreateAsync(modelManager, RapidOcrVersion.V5);
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
