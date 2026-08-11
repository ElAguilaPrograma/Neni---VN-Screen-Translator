namespace Neni.Abstractions.Entities;

public record WindowBounds(int X, int Y, int Width, int Height);

public record TranslationOverlayItem(
    int OverlayItemId,
    string OriginalText,
    string TranslatedText,
    WindowBounds Bounds // En principio esto se puedo obtener directamtente de la salida de OCR, son las rois pero dibujadas en el overlay
);