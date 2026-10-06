using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;

namespace Neni.Platform.Services.Linux;

// Selector de ventana objetivo en Linux.
//
// Va siempre por xdg-desktop-portal, tambien bajo X11: el portal funciona igual en las dos
// sesiones y nos ahorra mantener un camino de P/Invoke a libX11 en paralelo. Enumerar ventanas
// con XQueryTree/XGetWMName queda como posible camino secundario para X11 sin portal
// (TODO: implementarlo solo si aparece un entorno X11 real sin xdg-desktop-portal instalado).
public sealed class LinuxCaptureTargetSelector : ICaptureTargetSelector
{
    private readonly PortalScreenCastSession _portalSession;

    public LinuxCaptureTargetSelector(PortalScreenCastSession portalSession)
        => _portalSession = portalSession;

    public TargetSelectionMode SelectionMode => TargetSelectionMode.NativePrompt;

    public Task<IEnumerable<CaptureTarget>> ListAvailableTargetsAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "En Linux la seleccion pasa por el dialogo del portal; usa PromptTargetSelectionAsync " +
            $"(SelectionMode == {nameof(TargetSelectionMode.NativePrompt)}).");

    public Task<CaptureTarget?> PromptTargetSelectionAsync(
        bool reuseLastSelection = false,
        CancellationToken cancellationToken = default)
        => _portalSession.RequestTargetAsync(reuseLastSelection, cancellationToken);

    /// <summary>
    /// No hace nada: la sesion del portal se comparte con LinuxFrameCapture y la libera su dueño,
    /// el contenedor, despues de la captura que depende de ella.
    /// </summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
