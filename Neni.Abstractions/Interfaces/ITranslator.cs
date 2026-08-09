using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Interfaces;

public interface ITranslator : IAsyncDisposable
{
    Task<string> TranslateAsync(string text, Languages sourceLanguage, Languages targetLanguage, CancellationToken cancellationToken = default);
}