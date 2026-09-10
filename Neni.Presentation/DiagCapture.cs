using System.Security.Cryptography;
using Neni.Platform.Services.Linux;

namespace Neni.Presentation;

// Diagnóstico TEMPORAL (no forma parte del flujo real de la app): pide frames directo del
// pipeline de captura real (GStreamer/PipeWire vía LinuxFrameCapture), SIN tocar Avalonia en
// absoluto. Sirve para aislar si "el preview no se actualiza" es un problema de renderizado
// (Image/WriteableBitmap) o de entrega de frames (LinuxFrameCapture/GStreamer): si el hash de
// los píxeles no cambia entre pulls aun con una fuente en movimiento visible, el problema está
// río arriba, no en la UI.
//
// Uso:
//   dotnet run --project Neni.Presentation -- --diag-capture           (pull loop vía LinuxFrameCapture)
//   dotnet run --project Neni.Presentation -- --diag-capture-holdonly  (solo abre el portal y sostiene
//     la sesión, SIN correr ningún pipeline propio, para que un gst-launch-1.0 externo sea el ÚNICO
//     consumidor del node id -- evita que nuestra propia conexión "compita" por el mismo nodo)
internal static class DiagCapture
{
    public static async Task RunAsync()
    {
        var portalSession = new PortalScreenCastSession();
        var selector = new LinuxCaptureTargetSelector(portalSession);
        var frameCapture = new LinuxFrameCapture(portalSession);

        Console.WriteLine("Elegí en el diálogo del portal la ventana con el video en movimiento...");
        var target = await selector.PromptTargetSelectionAsync();

        if (target is null)
        {
            Console.WriteLine("Selección cancelada.");
            return;
        }

        Console.WriteLine($"Adjuntando a '{target.Name}' (PipeWire node {target.PipeWireNodeId})...");
        await frameCapture.AttachToTargetAsync(target);

        Console.WriteLine("Pulleando 20 frames cada 200ms (deberían tomar ~4s en total)...");
        for (var i = 0; i < 20; i++)
        {
            var frame = await frameCapture.GrabFrameAsync();
            var hash = Convert.ToHexString(SHA256.HashData(frame.PixelData.Span))[..16];
            Console.WriteLine($"[{i:00}] {frame.Width}x{frame.Height} hash={hash}");
            await Task.Delay(200);
        }

        await frameCapture.DisposeAsync();
        Console.WriteLine("Listo.");
    }

    // Igual que RunAsync pero sin crear NUNCA nuestro propio pipewiresrc: la sesión del portal
    // queda viva únicamente para mantener el node id válido, para que un gst-launch-1.0 externo
    // sea el único consumidor real de ese nodo (evita el escenario de dos pipewiresrc compitiendo
    // por el mismo nodo, que podría explicar por sí solo un freeze).
    public static async Task RunHoldOnlyAsync()
    {
        var portalSession = new PortalScreenCastSession();
        var selector = new LinuxCaptureTargetSelector(portalSession);

        Console.WriteLine("Elegí en el diálogo del portal la ventana/pantalla con contenido en movimiento...");
        var target = await selector.PromptTargetSelectionAsync();

        if (target is null)
        {
            Console.WriteLine("Selección cancelada.");
            return;
        }

        Console.WriteLine($"NODE_ID={target.PipeWireNodeId}");
        Console.WriteLine("Sesión mantenida viva 45s, SIN pipeline propio, para probar un consumidor externo único...");
        await Task.Delay(TimeSpan.FromSeconds(45));
        Console.WriteLine("Listo.");
    }
}
