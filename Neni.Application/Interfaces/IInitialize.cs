using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;

namespace Neni.Application.Interfaces;

internal interface IInitialize : IAsyncDisposable
{
    Settings AppSettings { get; }
    IOcr Engine { get; }
    ITranslator Translator { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
}
