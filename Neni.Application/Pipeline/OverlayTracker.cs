using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;

namespace Neni.Application.Pipeline;

// Lleva los bloques traducidos al overlay: los pasa a coordenadas de ventana, decide que item crear,
// actualizar o quitar comparando con lo que cada ROI tenia en pantalla, y lo recuerda por RoiId.
// Solo lo usa el hilo del ciclo.
internal sealed class OverlayTracker
{
    private readonly IOverlay _overlay;
    // Ids de overlay actualmente en pantalla, por RoiId. Una ROI puede tener varios items, uno por
    // bloque de texto detectado.
    private readonly Dictionary<int, HashSet<int>> _activeIdsByRoi = new();

    public OverlayTracker(IOverlay overlay)
    {
        _overlay = overlay;
    }

    /// <summary>
    /// Sincroniza el overlay de una ROI con sus bloques actuales: crea los nuevos, actualiza los que
    /// ya estaban y quita los que desaparecieron. No hace nada en modo ventana de acompañante.
    /// </summary>
    public async Task ApplyAsync(RegionOfInterest roi, IReadOnlyList<TranslatedBlock> blocks)
    {
        if (_overlay.CurrentOverlayCapability == OverlayCapability.CompanionWindowOnly)
            return;

        var previousIds = _activeIdsByRoi.GetValueOrDefault(roi.RoiId);
        var currentIds = new HashSet<int>();
        var newItems = new List<TranslationOverlayItem>();

        foreach (var block in blocks)
        {
            var itemId = MakeOverlayItemId(roi.RoiId, block.Index);
            currentIds.Add(itemId);

            var item = new TranslationOverlayItem(
                itemId, block.OriginalText, block.TranslatedText, ToWindowBounds(roi, block.BoxPoints));

            if (previousIds is not null && previousIds.Contains(itemId))
                _overlay.UpdateOverlayContent(item);
            else
                newItems.Add(item);
        }

        if (newItems.Count > 0)
            await _overlay.RenderTranslationOverlayAsync(newItems);

        // Bloques que estaban en pantalla y ya no aparecieron (opcion de menu cerrada, texto acortado).
        if (previousIds is not null)
        {
            foreach (var staleId in previousIds.Except(currentIds))
                _overlay.RemoveOverlayContent(staleId);
        }

        _activeIdsByRoi[roi.RoiId] = currentIds;
    }

    /// <summary>Quita de pantalla todo lo de una ROI y la olvida.</summary>
    public void Forget(int roiId)
    {
        if (!_activeIdsByRoi.Remove(roiId, out var ids))
            return;

        foreach (var id in ids)
            _overlay.RemoveOverlayContent(id);
    }

    /// <summary>Olvida todo sin tocar el overlay; para despues de IOverlay.StopAsync.</summary>
    public void Reset() => _activeIdsByRoi.Clear();

    /// <summary>
    /// Pasa una caja relativa al recorte a coordenadas de ventana sumando el origen del recorte, que
    /// FrameProcessor redondea hacia abajo. Usa min/max de las cuatro esquinas, no asume su orden.
    /// </summary>
    internal static WindowBounds ToWindowBounds(RegionOfInterest roi, IReadOnlyList<TextPoint> boxPoints)
    {
        var cropLeft = (int)Math.Floor(roi.X);
        var cropTop = (int)Math.Floor(roi.Y);

        var minX = boxPoints.Min(p => p.X);
        var minY = boxPoints.Min(p => p.Y);
        var maxX = boxPoints.Max(p => p.X);
        var maxY = boxPoints.Max(p => p.Y);

        return new WindowBounds(
            cropLeft + (int)minX,
            cropTop + (int)minY,
            (int)(maxX - minX),
            (int)(maxY - minY));
    }

    // Id deterministico y estable para el item de un bloque dentro de una ROI. Asume RoiId pequeño
    // (Settings.MaxPendingRois = 8) y como mucho unos cientos de bloques por ROI.
    private static int MakeOverlayItemId(int roiId, int blockIndex) => roiId * 1000 + blockIndex;
}
