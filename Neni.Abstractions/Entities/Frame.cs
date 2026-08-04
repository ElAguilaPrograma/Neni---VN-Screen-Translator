using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Entities;

public sealed record Frame(
    ReadOnlyMemory<byte> PixelData,
    int Width,
    int Height,
    int Stride,
    PixelFormat Format);
    