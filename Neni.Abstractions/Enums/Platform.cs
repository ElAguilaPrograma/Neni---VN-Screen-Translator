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

// Como puede ofrecerse la seleccion de la ventana objetivo en esta plataforma. Permite a la capa
// de aplicacion/UI decidir sin tener que preguntar por el servidor grafico ni conocer Wayland.
public enum TargetSelectionMode
{
    // Podemos enumerar las ventanas del sistema y la UI arma su propio selector (Windows, X11 nativo).
    Enumerable,
    // Hay que invocar el selector nativo del sistema y quedarnos con lo que devuelva
    // (Wayland via xdg-desktop-portal): no existe forma de listar ventanas ajenas.
    NativePrompt
}

