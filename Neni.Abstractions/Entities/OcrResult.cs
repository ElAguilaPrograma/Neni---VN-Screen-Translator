namespace Neni.Abstractions.Entities;

// Punto 2D neutral
public readonly record struct TextPoint(float X, float Y);

// Linea de texto detectada, con su posición original en el frame
public sealed record OcrTextBlock(
    string Text,
    IReadOnlyList<TextPoint> BoxPoints, // 4 esquinas en el sentido horario
    float Confidence); // promedio de CharScores
    
// Resultado completo de una detección del OCR sobre un frame
public sealed record OcrResult(IReadOnlyList<OcrTextBlock> Blocks)
{
    public string FullText => string.Join(Environment.NewLine, Blocks.Select(b => b.Text));
    public static readonly OcrResult Empty = new([]);
}