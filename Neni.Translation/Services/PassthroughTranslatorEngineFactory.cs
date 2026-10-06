using Neni.Abstractions.Interfaces;

namespace Neni.Translation.Services;

// TODO pendiente: reemplazar por el factory real (tokenizer SentencePiece + inferencia ONNX)
// cuando se implemente Opus-MT/Marian. Ver PassthroughTranslator.
internal sealed class PassthroughTranslatorEngineFactory : ITranslatorEngineFactory
{
    public Task<ITranslator> CreateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<ITranslator>(new PassthroughTranslator());
}
