using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;

namespace Neni.Translation.Services;

// Placeholder TEMPORAL: existe solo para que Neni.Application.Services.Initialize/Coordinator
// puedan construirse por DI mientras Opus-MT/Marian no esté implementado.
internal sealed class NotImplementedTranslator : ITranslator
{
    public Task<string> TranslateAsync(string text, Languages sourceLanguage, Languages targetLanguage, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Neni.Translation todavía no implementa Opus-MT/Marian.");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
