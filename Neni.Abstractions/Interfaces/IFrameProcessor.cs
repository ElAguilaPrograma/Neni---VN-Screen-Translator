using Neni.Abstractions.Entities;

namespace Neni.Abstractions.Interfaces;

// Logica de imagen pura, sin dependencia de plataforma: recorte y preprocesamiento.
// Implementado por la capa Imaging
public interface IFrameProcessor
{
    // Recorta el frame capturado a una region de interes, devolviendo el recorte.
    // Las coordenadas del recorte son relativas a la ROI, no a la ventana: si se necesita
    // volver a espacio de ventana hay que sumarles el origen de la ROI (roi.X/Y).
    Frame CropFrame(Frame frame, RegionOfInterest roi);

    // Aplica tecnicas de preprocesamiento al frame recortado para mejorar la calidad de la imagen antes de enviarlo al motor OCR.
    Frame ProcessFrame(Frame frame);
}
