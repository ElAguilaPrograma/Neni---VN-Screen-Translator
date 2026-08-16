namespace Neni.Abstractions.Entities;

public record CaptureTarget(
    string Id,
    string Name,
    IntPtr NativeHandle = default, // Handle nativo en windows y X11, 0 en wayland.
    uint? PipeWireNodeId = null // Node ID de pipewire, null en windows.
)
{
    public bool IsValid => NativeHandle != IntPtr.Zero || PipeWireNodeId.HasValue;
}