namespace Neni.Abstractions.Interfaces;

public interface ITranslatorEngineFactory
{
    // La implementación de este metodo debe ser un metodo que redireccione al metodo CreateAsync 
    // en la capa de traducción, para que se pueda crear una instancia de la clase que implementa 
    // la interfaz ITranslator. )
    Task<ITranslator> CreateAsync(CancellationToken cancellationToken = default);
}