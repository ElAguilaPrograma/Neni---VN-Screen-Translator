namespace Neni.Abstractions.Enums;

// TODO en linux el construntor que construya la aplicación debera de detectar la plataforma,
// y si es linux, validara si es X11(Click through) o Wayland, si es wayland validara si soporta LayerShell para el overlay
// si no soporta LayerShell, fallback a modo ventana de acompañante
public enum DisplayServerType
{
    WindowsDesktop,
    X11,
    Wayland,
    Unknown
}

public enum OverlayCapability
{
    DirectPassthroughOverlay,
    LayerShellOverlay,
    CompanionWindowOnly
}

