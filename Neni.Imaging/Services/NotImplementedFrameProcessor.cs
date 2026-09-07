using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;

namespace Neni.Imaging.Services;

// Placeholder TEMPORAL: existe solo para que Coordinator pueda construirse por DI.
// No se invoca todavía desde ningún flujo real (Select Window no recorta frames).
public sealed class NotImplementedFrameProcessor : IFrameProcessor
{
    public Frame CropFrames(Frame frame, Abstractions.Entities.RegionOfInterest rois)
        => throw new NotImplementedException();

    public Frame ProcessFrames(Frame frames)
        => throw new NotImplementedException();
}
