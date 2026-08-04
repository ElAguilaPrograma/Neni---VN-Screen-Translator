using Neni.Abstractions.Entities;

namespace Neni.Abstractions.Interfaces;

public interface IOcrEngine : IAsyncDisposable
{
    Task<OcrResult> DetectAsync(Frame frame, CancellationToken cancellationToken = default);
}