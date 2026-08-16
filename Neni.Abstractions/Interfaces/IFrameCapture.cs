using Neni.Abstractions.Entities;

namespace Neni.Abstractions.Interfaces;

// Implementado por capa de plataforma (por SO). En Linux/Wayland esto puede requerir
// el flujo async de xdg-desktop-portal (dialogo de permiso + stream de PipeWire),
// de ahi que sea async incluso cuando la implementacion de X11/Windows sea sincrona por dentro.
// Hereda IAsyncDisposable (igual que IOverlay) porque la implementacion sostiene recursos
// nativos vivos entre ciclos: una tuberia de GStreamer y un fd de PipeWire.
public interface IFrameCapture : IAsyncDisposable
{
    // Vincula la ventana objetivo a la sesión de captura (sea por Win32 handle o PipeWire Node ID).
    Task AttachToTargetAsync(CaptureTarget target, CancellationToken cancellationToken = default);
    // Recoge el frame de la ventana objetivo y lo devuelve como un objeto Frame.
    Task<Frame> GrabFrameAsync(CancellationToken cancellationToken = default);
}
