using Neni.Abstractions.Entities;

namespace Neni.Abstractions.Interfaces;

public interface IOcrEngine
{
    Task<OcrResult> ExtractTextAsync(Frame frame, CancellationToken cancellationToken = default);
}