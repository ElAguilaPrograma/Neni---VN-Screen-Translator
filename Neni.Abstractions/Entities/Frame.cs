using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Entities;

public class Frame
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Stride { get; set; }
    public PixelFormat Format { get; set; }
}