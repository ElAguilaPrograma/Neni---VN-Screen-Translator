using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;

namespace Neni.Translation.Services;

// TODO pendiente: Opus-MT/Marian (tokenizer SentencePiece + inferencia ONNX). Hasta entonces
// devuelve el texto de entrada sin tocarlo, igual que el stub de Ocr.NormalizeText, para poder
// ejercitar el ciclo completo mostrando el texto detectado sin traducir.
internal sealed class PassthroughTranslator : ITranslator
{
    public Task<string> TranslateAsync(string text, Languages sourceLanguage, Languages targetLanguage, CancellationToken cancellationToken = default)
        => Task.FromResult(text);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
