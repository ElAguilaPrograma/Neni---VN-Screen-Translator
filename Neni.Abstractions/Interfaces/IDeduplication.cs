using Neni.Abstractions.Entities;

namespace Neni.Abstractions.Interfaces;

public interface IDeduplication
{
    bool IsDuplicate(Frame frame, Frame? previousFrame);
}