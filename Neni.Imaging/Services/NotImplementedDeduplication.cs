using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;

namespace Neni.Imaging.Services;

// Placeholder TEMPORAL: existe solo para que Coordinator pueda construirse por DI.
// No se invoca todavía desde ningún flujo real (Select Window no procesa ciclos).
public sealed class NotImplementedDeduplication : IDeduplication
{
    public bool IsDuplicate(Frame frame, Frame? previousFrame)
        => throw new NotImplementedException();
}
