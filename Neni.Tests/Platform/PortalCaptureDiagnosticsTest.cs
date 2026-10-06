using System.Security.Cryptography;
using Neni.Platform.Services.Linux;
using Xunit.Abstractions;

namespace Neni.Tests.Platform;

// Diagnosticos manuales del bug upstream de xdg-desktop-portal-hyprland (#423): el screencast
// entrega 1-2 frames y se congela para siempre. Sirven para comprobar si una actualizacion del
// portal ya trae el fix (PR #424), aislando la captura de Avalonia y del resto de la pipeline.
//
// Uso (elegir en el dialogo una ventana con contenido en movimiento, p. ej. un video):
//   NENI_MANUAL_TESTS=1 dotnet test --filter "FullyQualifiedName~PortalCaptureDiagnosticsTest" --logger "console;verbosity=detailed"
public class PortalCaptureDiagnosticsTest
{
    private const int FramesToPull = 20;
    private static readonly TimeSpan PullInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(45);

    private readonly ITestOutputHelper _output;

    public PortalCaptureDiagnosticsTest(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Pide frames por la ruta real de captura (LinuxFrameCapture) y falla si el contenido deja de
    /// cambiar tras los primeros frames, que es la firma del bug del portal.
    /// </summary>
    [ManualFact]
    public async Task GrabFrameAsync_sigue_entregando_frames_nuevos_con_fuente_en_movimiento()
    {
        // Declarada primero para que se libere al final, despues de la captura que depende de ella.
        await using var portalSession = new PortalScreenCastSession();
        var selector = new LinuxCaptureTargetSelector(portalSession);
        await using var frameCapture = new LinuxFrameCapture(portalSession);

        var target = await selector.PromptTargetSelectionAsync();
        Assert.NotNull(target);

        _output.WriteLine($"Adjuntando a '{target.Name}' (PipeWire node {target.PipeWireNodeId})");
        await frameCapture.AttachToTargetAsync(target);

        var hashes = new List<string>();
        for (var i = 0; i < FramesToPull; i++)
        {
            var frame = await frameCapture.GrabFrameAsync();
            var hash = Convert.ToHexString(SHA256.HashData(frame.PixelData.Span))[..16];
            hashes.Add(hash);
            _output.WriteLine($"[{i:00}] {frame.Width}x{frame.Height} hash={hash}");
            await Task.Delay(PullInterval);
        }

        // Con el bug solo aparecen 1-2 hashes distintos; con el fix cambian casi en cada pull.
        var distinct = hashes.Distinct().Count();
        Assert.True(distinct > 2,
            $"Solo {distinct} frames distintos en {FramesToPull} pulls: el screencast parece congelado (portal #423).");
    }

    /// <summary>
    /// Abre el portal y mantiene viva la sesion sin crear ningun pipeline propio, para que un
    /// gst-launch-1.0 externo sea el unico consumidor del nodo durante HoldDuration.
    /// </summary>
    [ManualFact]
    public async Task Sesion_del_portal_se_mantiene_viva_para_un_consumidor_externo()
    {
        await using var portalSession = new PortalScreenCastSession();
        var selector = new LinuxCaptureTargetSelector(portalSession);

        var target = await selector.PromptTargetSelectionAsync();
        Assert.NotNull(target);

        // Se escribe tambien a stderr porque la salida del test solo se ve al terminar, y el
        // node id hace falta mientras la sesion sigue viva.
        Console.Error.WriteLine($"NODE_ID={target.PipeWireNodeId}");
        _output.WriteLine($"NODE_ID={target.PipeWireNodeId}");

        await Task.Delay(HoldDuration);
    }
}
