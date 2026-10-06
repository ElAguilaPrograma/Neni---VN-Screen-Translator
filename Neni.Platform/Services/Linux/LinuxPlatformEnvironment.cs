using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;

namespace Neni.Platform.Services.Linux;

internal sealed class LinuxPlatformEnvironment : IPlatformEnvironment
{
    // Compositores cuyo servidor implementa zwlr_layer_shell_v1, que es lo que nos permite
    // pintar un overlay por encima del juego. Mutter (GNOME) no lo implementa.
    private static readonly HashSet<string> LayerShellDesktops =
        new(StringComparer.OrdinalIgnoreCase) { "hyprland", "sway", "river", "niri", "wlroots", "kde", "plasma" };

    public DisplayServerType GetDisplayServerType()
    {
        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")?.ToLowerInvariant();

        if (sessionType == "wayland")
            return DisplayServerType.Wayland;

        if (sessionType == "x11")
            return DisplayServerType.X11;

        // XDG_SESSION_TYPE no siempre esta definido (sesiones por SSH, algunos lanzadores),
        // asi que caemos a las variables del propio servidor grafico.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            return DisplayServerType.Wayland;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            return DisplayServerType.X11;

        return DisplayServerType.Unknown;
    }

    public OverlayCapability GetOverlayCapability()
        => GetDisplayServerType() switch
        {
            DisplayServerType.X11 => OverlayCapability.DirectPassthroughOverlay,
            DisplayServerType.Wayland when SupportsLayerShell() => OverlayCapability.LayerShellOverlay,
            _ => OverlayCapability.CompanionWindowOnly
        };

    public bool SupportsClickThrough()
        => GetOverlayCapability() != OverlayCapability.CompanionWindowOnly;

    // XDG_CURRENT_DESKTOP es una LISTA separada por ':' ("wlroots:sway", "GNOME:GNOME-Classic"),
    // asi que hay que partirla; comparar la cadena entera falla en la mitad de los escritorios.
    private static bool SupportsLayerShell()
    {
        var currentDesktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");

        if (string.IsNullOrEmpty(currentDesktop))
            return false;

        var desktops = currentDesktop.Split(
            ':',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return desktops.Any(LayerShellDesktops.Contains);
    }
}
