using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;

namespace Neni.Application.Interfaces;

internal interface IInitialize : IAsyncDisposable
{
    // true solo cuando InitializeAsync termino bien; AppSettings existe desde la construccion.
    bool IsInitialized { get; }
    Settings AppSettings { get; }
    IOcr Engine { get; }
    ITranslator Translator { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
}
