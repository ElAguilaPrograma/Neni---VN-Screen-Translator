using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Interfaces;

public interface ITranslator : IAsyncDisposable
{
    // Traduce el texto proporcionado del idioma de origen al idioma de destino, devolviendo el texto traducido.
    Task<string> TranslateAsync(string text, Languages sourceLanguage, Languages targetLanguage, CancellationToken cancellationToken = default);
}