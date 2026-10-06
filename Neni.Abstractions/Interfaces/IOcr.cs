using Neni.Abstractions.Entities;

namespace Neni.Abstractions.Interfaces;

public interface IOcr : IAsyncDisposable
{
    // Detecta el texto en la imagen proporcionada y devuelve el resultado del OCR.
    Task<OcrResult> DetectAsync(Frame frame, CancellationToken cancellationToken = default);
}