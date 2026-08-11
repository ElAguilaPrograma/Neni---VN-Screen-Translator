using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;

namespace Neni.Application.Interfaces;

public interface IInitialize
{
    Settings AppSettings { get; }
    IOcr Engine { get; }
    ITranslator Translator { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task DisposeAsync();
}