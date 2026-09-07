using Neni.Abstractions.Interfaces;

namespace Neni.Translation.Services;

// Placeholder TEMPORAL, ver NotImplementedTranslator. Reemplazar por el factory real
// (tokenizer SentencePiece + inferencia ONNX) cuando se implemente Neni.Translation.
public sealed class NotImplementedTranslatorEngineFactory : ITranslatorEngineFactory
{
    public Task<ITranslator> CreateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<ITranslator>(new NotImplementedTranslator());
}
