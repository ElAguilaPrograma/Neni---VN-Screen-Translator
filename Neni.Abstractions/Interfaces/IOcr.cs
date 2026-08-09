using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Interfaces;

public interface IOcr : IAsyncDisposable
{
    Task<OcrResult> DetectAsync(Frame frame, CancellationToken cancellationToken = default);
    string NormalizeText(string text, Languages sourceLanguage);
}