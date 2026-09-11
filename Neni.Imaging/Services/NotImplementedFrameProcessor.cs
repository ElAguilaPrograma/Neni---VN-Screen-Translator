using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;

namespace Neni.Imaging.Services;

// Placeholder TEMPORAL: existe solo para que Coordinator pueda construirse por DI.
// No se invoca todavía desde ningún flujo real (Select Window no recorta frames).
public sealed class NotImplementedFrameProcessor : IFrameProcessor
{
    public Frame CropFrame(Frame frame, Abstractions.Entities.RegionOfInterest roi)
        => throw new NotImplementedException();

    public Frame ProcessFrame(Frame frame)
        => throw new NotImplementedException();
}
