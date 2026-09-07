using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;

namespace Neni.Translation.Services;

// Placeholder TEMPORAL: existe solo para que Neni.Application.Services.Initialize/Coordinator
// puedan construirse por DI mientras Opus-MT/Marian no esté implementado (ver CLAUDE.md,
// "Implementation ownership decisions"). No se invoca todavía desde ningún flujo real
// (Select Window solo usa OpenWindowSelectorAsync/AttachToTargetAsync, que no tocan el traductor).
internal sealed class NotImplementedTranslator : ITranslator
{
    public Task<string> TranslateAsync(string text, Languages sourceLanguage, Languages targetLanguage, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Neni.Translation todavía no implementa Opus-MT/Marian.");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
