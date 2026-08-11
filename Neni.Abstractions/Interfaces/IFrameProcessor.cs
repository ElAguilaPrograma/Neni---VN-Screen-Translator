using Neni.Abstractions.Entities;

namespace Neni.Abstractions.Interfaces;

// Logica de imagen pura, sin dependencia de plataforma: recorte y preprocesamiento.
// Implementado por la capa Plataforma
public interface IFrameProcessor
{
    // Usa el frame recogido y lo recorta a las regiones de interes indicadas, devolviendo una colección de frames con los recortes.
    Frame CropFrames(Frame frame, RegionOfInterest rois);

    // Aplica tecnicas de preprocesamiento a los frames recortados para mejorar la calidad de la imagen antes de enviarlos al motor OCR.
    Frame ProcessFrames(Frame frames);
}
