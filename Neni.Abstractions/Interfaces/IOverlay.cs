using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Interfaces;

public interface IOverlay : IAsyncDisposable
{
    Task InitializeAsync(IntPtr targetWindowHandle);
    Task RenderTranslationOverlayAsync(IEnumerable<TranslationOverlayItem> items);
    OverlayCapability CurrentOverlayCapability { get; }
}