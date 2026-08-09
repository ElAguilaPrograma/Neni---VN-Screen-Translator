using Neni.Abstractions.Entities;

namespace Neni.Abstractions.Interfaces;

// Implementado por capa de plataforma (por SO). En Linux/Wayland esto puede requerir
// el flujo async de xdg-desktop-portal (dialogo de permiso + stream de PipeWire),
// de ahi que sea async incluso cuando la implementacion de X11/Windows sea sincrona por dentro.
public interface IFrameCapture
{
    // Recoge el frame de la ventana objetivo y lo devuelve como un objeto Frame.
    Task<Frame> GrabFrameAsync(CancellationToken cancellationToken = default);
}
