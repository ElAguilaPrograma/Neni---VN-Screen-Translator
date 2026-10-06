namespace Neni.Abstractions.Entities;

// Huella reducida de un frame que produce IDeduplication y que solo ella sabe comparar. Es opaca a
// proposito: quien la recibe solo la guarda y se la devuelve; su contenido depende del algoritmo de
// deduplicacion de cada implementacion.
public abstract record FrameSignature;
