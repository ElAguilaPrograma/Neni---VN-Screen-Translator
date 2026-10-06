using Neni.Abstractions.Entities;

namespace Neni.Imaging.Services;

// Firma de Deduplication. Guardar esto en vez del recorte entero evita recalcular gris/bordes del
// frame anterior en cada ciclo. QuantizedGray y Edges son ambos de Width * Height bytes.
internal sealed record EdgeFrameSignature(
    byte[] QuantizedGray, // gris reducido y cuantizado: detecta "no cambio absolutamente nada"
    byte[] Edges,         // mascara 0/1 de bordes: detecta "cambio algo estructural, no solo color"
    int Width,
    int Height) : FrameSignature;
