using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Interfaces;

public interface IOcr : IAsyncDisposable
{
    // Detecta el texto en la imagen proporcionada y devuelve el resultado del OCR.
    Task<OcrResult> DetectAsync(Frame frame, CancellationToken cancellationToken = default);
    // Normaliza el texto detectado, eliminando caracteres no deseados y aplicando reglas de normalización específicas para el idioma de origen.
    string NormalizeText(string text, Languages sourceLanguage);
}