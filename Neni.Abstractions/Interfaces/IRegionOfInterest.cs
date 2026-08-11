using Neni.Abstractions.Entities;

namespace Neni.Abstractions.Interfaces;

public interface IRegionOfInterest
{
    // Dibuja las regiones de interés (ROIs) en el frame proporcionado y devuelve un objeto RegionOfInterest que contiene información sobre las ROIs dibujadas.
    Task<RegionOfInterest> DrawRoisAsync(Frame frame, CancellationToken cancellationToken = default);
    // Elimina una región de interés específica identificada por su ID.
    void DeleteRoi(int roiId);
    // Limpia todas las regiones de interés (ROIs) actualmente definidas.
    void ClearRois();
}