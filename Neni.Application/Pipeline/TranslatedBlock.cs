using Neni.Abstractions.Entities;

namespace Neni.Application.Pipeline;

// Un bloque de texto de una ROI ya normalizado y traducido. Index es su posicion en orden de
// lectura dentro de la ROI (estable entre vueltas); BoxPoints es relativo al RECORTE, no a la ventana.
internal sealed record TranslatedBlock(
    int Index,
    string OriginalText,
    string TranslatedText,
    IReadOnlyList<TextPoint> BoxPoints);
