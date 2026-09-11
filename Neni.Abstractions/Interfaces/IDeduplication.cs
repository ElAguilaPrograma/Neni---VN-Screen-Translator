using Neni.Abstractions.Entities;

namespace Neni.Abstractions.Interfaces;

public interface IDeduplication
{
    // Reduce el frame a su firma comparable. Se separa de IsDuplicate para que el llamador pueda
    // guardar la firma SOLO cuando despacha a OCR.
    FrameSignature ComputeSignature(Frame frame);

    // true si la ROI no cambio lo suficiente como para justificar otro OCR.
    // previous debe ser la firma del ultimo frame DESPACHADO a OCR, no la del ciclo
    // anterior. Con el efecto typewriter de las VN cada letra es un delta minusculo que nunca cruza
    // el umbral por si solo, y comparando contra el ciclo anterior el dialogo podria revelarse
    // entero sin disparar un solo OCR.
    bool IsDuplicate(FrameSignature current, FrameSignature? previous);
}
