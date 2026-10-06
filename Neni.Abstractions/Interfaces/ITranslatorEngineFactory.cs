namespace Neni.Abstractions.Interfaces;

public interface ITranslatorEngineFactory
{
    // Crea un traductor listo para usar; el llamador es su dueño y debe liberarlo.
    Task<ITranslator> CreateAsync(CancellationToken cancellationToken = default);
}
