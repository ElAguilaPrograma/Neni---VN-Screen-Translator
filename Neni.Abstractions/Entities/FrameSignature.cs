namespace Neni.Abstractions.Entities;

// Huella reducida de un frame, se usa para decidir si vale la pena volver a correr el OCR sobre una
// ROI. Guardar esto en vez del recorte entero evita recalcular gris/bordes del frame anterior en
// cada ciclo. QuantizedGray y Edges son ambos de Width * Height bytes.
public sealed record FrameSignature(
    byte[] QuantizedGray, // gris reducido y cuantizado: detecta "no cambio absolutamente nada"
    byte[] Edges,         // mascara 0/1 de bordes: detecta "cambio algo estructural, no solo color"
    int Width,
    int Height);
