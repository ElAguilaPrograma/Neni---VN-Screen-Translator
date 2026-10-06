using Neni.Abstractions.Entities;

namespace Neni.Presentation.Services;

// Dibujar ROIs es interaccion pura de UI: este servicio no es un puerto de Application, la UI lo
// usa para obtener las ROIs y despues se las entrega al ICoordinator, que es su dueno.
internal interface IRoiDrawingDialog
{
    /// <summary>
    /// Abre la ventana de dibujo sobre el frame, con las ROIs actuales como borrador. Devuelve las
    /// ROIs confirmadas, o null si se cerro sin confirmar.
    /// </summary>
    Task<IReadOnlyList<RegionOfInterest>?> ShowAsync(
        Frame frame,
        IReadOnlyList<RegionOfInterest> currentRois,
        CancellationToken cancellationToken = default);
}
