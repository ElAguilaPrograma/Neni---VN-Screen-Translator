using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;

namespace Neni.Presentation.Services;

// Placeholder TEMPORAL: existe solo para que Coordinator pueda construirse por DI.
// No se invoca todavía desde ningún flujo real (Select Window no arranca el ciclo de overlay).
// Reemplazar por la implementación real en Avalonia (ver CLAUDE.md, "Implementation ownership
// decisions": IOverlay vive en esta capa porque dibujar ventanas es algo que Avalonia resuelve
// directamente).
internal sealed class NoOpOverlay : IOverlay
{
    public OverlayCapability CurrentOverlayCapability => OverlayCapability.CompanionWindowOnly;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public Task RenderTranslationOverlayAsync(IEnumerable<TranslationOverlayItem> items) => Task.CompletedTask;

    public void UpdateOverlayContent(TranslationOverlayItem item)
    {
    }

    public void RemoveOverlayContent(int itemId)
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
