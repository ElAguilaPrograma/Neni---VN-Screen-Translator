using Neni.Abstractions.Interfaces;
using Neni.Abstractions.Entities;

namespace Neni.Imaging.Services;

public class RegionOfInterest : IRegionOfInterest
{
    public async Task<IEnumerable<Abstractions.Entities.RegionOfInterest>> DrawRoisAsync(Frame frame, CancellationToken cancellationToken = default)
    {
        // Implementación para dibujar las regiones de interés (ROIs) en el frame proporcionado.
        throw new NotImplementedException();
    }

    public void DeleteRoi(int roiId)
    {
        // Implementación para eliminar una región de interés específica identificada por su ID.
        throw new NotImplementedException();
    }

    public void ClearRois()
    {
        // Implementación para limpiar todas las regiones de interés (ROIs) actualmente definidas.
        throw new NotImplementedException();
    }
}