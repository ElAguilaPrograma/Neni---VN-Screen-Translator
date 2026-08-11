namespace Neni.Abstractions.Interfaces;

public interface IOcrEngineFactory
{
    // Crea una unica instancia del motor OCR, que puede ser reutilizada para múltiples operaciones de OCR. 
    Task<IOcr> CreateAsync(CancellationToken cancellationToken = default);
}
