using Neni.Abstractions.Interfaces;

namespace Neni.Application.Interfaces;

public interface IInitialize
{
    ISettings Setting { get; set; }
    IOcr Engine { get; }
    ITranslator Translator { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task DisposeAsync();
}