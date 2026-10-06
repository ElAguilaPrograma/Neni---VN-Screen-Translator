namespace Neni.Application.Interfaces;

// Estado de una ROI en el reporte de cada vuelta. Text es lo ultimo que se proceso bien; Error trae
// el motivo si el ultimo intento fallo (Text sigue siendo el anterior), y vuelve a null en cuanto la
// ROI se procesa bien otra vez.
public sealed record RoiReport(string Text, string? Error = null);
