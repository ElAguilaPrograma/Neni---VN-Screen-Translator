using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Interfaces;

public interface ICaptureTargetSelector
{
    // Indica cual de los dos metodos de abajo tiene sentido en esta plataforma, para que la capa
    // de aplicacion no tenga que razonar sobre X11/Wayland/Windows por su cuenta.
    TargetSelectionMode SelectionMode { get; }

    // Windows / X11: obtiene la lista de ventanas para que la UI arme su propio selector.
    // Lanza NotSupportedException donde no se pueda enumerar (Wayland).
    Task<IEnumerable<CaptureTarget>> ListAvailableTargetsAsync(CancellationToken cancellationToken = default);

    // Invoca la API nativa de seleccion. Devuelve null si el usuario cancela.
    // En Linux: invoca xdg-desktop-portal via D-Bus y retorna el PipeWire Node ID elegido.
    // En Windows: puede activar un selector interactivo con la mira del mouse (Crosshair picker).
    //
    // reuseLastSelection: reutiliza el permiso concedido en una ejecucion anterior, si lo hay, para
    // reenganchar a la misma ventana sin volver a mostrar el dialogo. Debe ir en false cuando el
    // usuario pide explicitamente elegir ventana, o se reabriria siempre la ventana antigua.
    Task<CaptureTarget?> PromptTargetSelectionAsync(
        bool reuseLastSelection = false,
        CancellationToken cancellationToken = default);
}
