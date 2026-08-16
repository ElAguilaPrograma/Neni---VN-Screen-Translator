using System.Diagnostics;
using System.Runtime.InteropServices;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;
using Neni.Platform.Services.Linux.Interop;
using static Neni.Platform.Services.Linux.Interop.GStreamerNative;

namespace Neni.Platform.Services.Linux;

// Captura de frames en Linux: convierte el nodo de PipeWire que negocio el portal en objetos
// Frame, tirando de GStreamer para la negociacion de formatos.
//
// Necesita la MISMA instancia de PortalScreenCastSession que uso el selector: el node id y el fd
// solo son validos mientras esa sesion siga abierta (ver PortalScreenCastSession).
public sealed class LinuxFrameCapture : IFrameCapture
{
    private const string AppSinkName = "neni_sink";
    private const int BytesPerPixel = 4;
    private const string ExpectedFormat = "BGRA";

    // El primer frame puede tardar en llegar mientras el stream arranca, asi que ahi si esperamos.
    private const ulong FirstPullTimeoutNs = 3UL * 1_000_000_000;

    // A partir del segundo, el ciclo no puede permitirse bloquearse: appsink va con max-buffers=1
    // y drop=true, o sea que cualquier frame producido desde la ultima llamada YA esta en la cola
    // y sale al instante. Si no hay ninguno (una VN parada en un dialogo no genera frames nuevos)
    // reutilizamos el ultimo, que ademas ahorra copiar 14 MB para nada; la deduplicacion del
    // Coordinator lo descartara igualmente.
    private const ulong PullTimeoutNs = 50UL * 1_000_000;

    private const ulong PrerollTimeoutNs = 3UL * 1_000_000_000;

    // El nodo del portal no siempre acepta conexiones en el instante en que Start devuelve:
    // el primer intento puede fallar con "target not found". Reintentamos dentro de este margen.
    private const int AttachTimeoutMs = 5_000;
    private const int AttachRetryDelayMs = 150;

    private static readonly Lazy<bool> GStreamerReady = new(
        () => { gst_init(IntPtr.Zero, IntPtr.Zero); return true; },
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly PortalScreenCastSession _portalSession;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IntPtr _pipeline;
    private IntPtr _appSink;
    private Frame? _lastFrame;
    private bool _disposed;

    public LinuxFrameCapture(PortalScreenCastSession portalSession)
        => _portalSession = portalSession;

    public async Task AttachToTargetAsync(CaptureTarget target, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);

        if (target.PipeWireNodeId is not { } nodeId)
            throw new ArgumentException(
                "En Linux el objetivo de captura tiene que traer un PipeWireNodeId del portal.", nameof(target));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            TearDownPipeline();
            await AttachWithRetryAsync(nodeId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Frame> GrabFrameAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_appSink == IntPtr.Zero)
                throw new InvalidOperationException(
                    "AttachToTargetAsync() debe ser invocado (y esperado) antes de capturar frames.");

            // Las llamadas nativas de GStreamer son bloqueantes: fuera del hilo del ciclo.
            return await Task.Run(PullFrame, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task AttachWithRetryAsync(uint nodeId, CancellationToken cancellationToken)
    {
        var description = BuildPipelineDescription(nodeId);
        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;

            try
            {
                await Task.Run(() => StartPipeline(description), cancellationToken);
                return;
            }
            catch (InvalidOperationException ex)
            {
                TearDownPipeline();

                if (stopwatch.ElapsedMilliseconds >= AttachTimeoutMs)
                    throw new InvalidOperationException(
                        $"No se pudo enganchar al nodo {nodeId} de PipeWire tras {attempts} intentos " +
                        $"en {stopwatch.ElapsedMilliseconds} ms.", ex);

                await Task.Delay(AttachRetryDelayMs, cancellationToken);
            }
        }
    }

    private string BuildPipelineDescription(uint nodeId)
    {
        var fileDescriptorPart = TryDuplicatePipeWireFd() is { } fileDescriptor
            ? $"fd={fileDescriptor} "
            : string.Empty;

        // Sin keepalive-time a proposito: reenviar el ultimo buffer solo nos haria copiar de nuevo
        // 14 MB identicos cada ciclo. Si no llega nada nuevo preferimos reutilizar el Frame anterior.
        return $"pipewiresrc {fileDescriptorPart}path={nodeId} do-timestamp=true " +
               $"! videoconvert " +
               $"! video/x-raw,format={ExpectedFormat} " +
               $"! appsink name={AppSinkName} max-buffers=1 drop=true sync=false";
    }

    // Le damos a pipewiresrc una copia del fd porque se queda con su propiedad y lo cierra;
    // el original lo sigue gestionando PortalScreenCastSession. Si no hay fd (o no se puede
    // duplicar) omitimos la propiedad: pipewiresrc se conecta al demonio de la sesion, que es
    // suficiente mientras la aplicacion no corra en un sandbox.
    private int? TryDuplicatePipeWireFd()
    {
        var handle = _portalSession.PipeWireRemote;

        if (handle is null || handle.IsInvalid)
            return null;

        var duplicated = dup(handle.DangerousGetHandle().ToInt32());
        return duplicated < 0 ? null : duplicated;
    }

    private void StartPipeline(string description)
    {
        _ = GStreamerReady.Value;

        _pipeline = gst_parse_launch(description, out var error);

        if (error != IntPtr.Zero)
        {
            var message = ReadGErrorMessage(error);
            g_error_free(error);
            throw new InvalidOperationException($"No se pudo construir la tuberia de GStreamer: {message}");
        }

        if (_pipeline == IntPtr.Zero)
            throw new InvalidOperationException("gst_parse_launch devolvio una tuberia nula.");

        _appSink = gst_bin_get_by_name(_pipeline, AppSinkName);

        if (_appSink == IntPtr.Zero)
            throw new InvalidOperationException($"No se encontro el appsink '{AppSinkName}' en la tuberia.");

        if (gst_element_set_state(_pipeline, GstStatePlaying) == GstStateChangeFailure)
            ThrowPipelineError("no se pudo poner la tuberia en PLAYING");

        // Esperamos a que la tuberia asiente el cambio de estado: aqui es donde aflora el
        // "target not found" cuando el nodo del portal todavia no acepta conexiones.
        if (gst_element_get_state(_pipeline, out _, out _, PrerollTimeoutNs) == GstStateChangeFailure)
            ThrowPipelineError("la tuberia no llego a PLAYING");
    }

    private Frame PullFrame()
    {
        ThrowIfPipelineFailed();

        var timeoutNs = _lastFrame is null ? FirstPullTimeoutNs : PullTimeoutNs;
        var sample = gst_app_sink_try_pull_sample(_appSink, timeoutNs);

        if (sample == IntPtr.Zero)
        {
            ThrowIfPipelineFailed();

            // Caso normal, no error: la ventana no ha cambiado desde el ciclo anterior.
            return _lastFrame
                ?? throw new InvalidOperationException(
                    "GStreamer no entrego ningun frame y todavia no hay ninguno anterior que reutilizar.");
        }

        try
        {
            _lastFrame = ToFrame(sample);
            return _lastFrame;
        }
        finally
        {
            gst_mini_object_unref(sample);
        }
    }

    private static Frame ToFrame(IntPtr sample)
    {
        var caps = gst_sample_get_caps(sample);

        if (caps == IntPtr.Zero)
            throw new InvalidOperationException("El sample de GStreamer no trae caps.");

        var structure = gst_caps_get_structure(caps, 0);

        if (gst_structure_get_int(structure, "width", out var width) == 0
            || gst_structure_get_int(structure, "height", out var height) == 0
            || width <= 0
            || height <= 0)
            throw new InvalidOperationException("Las caps de GStreamer no traen un tamaño valido.");

        var format = Marshal.PtrToStringUTF8(gst_structure_get_string(structure, "format"));

        if (format != ExpectedFormat)
            throw new InvalidOperationException(
                $"Se esperaba formato {ExpectedFormat} y GStreamer negocio '{format}'.");

        var buffer = gst_sample_get_buffer(sample);

        if (buffer == IntPtr.Zero)
            throw new InvalidOperationException("El sample de GStreamer no trae buffer.");

        if (gst_buffer_map(buffer, out var map, GstMapRead) == 0)
            throw new InvalidOperationException("No se pudo mapear el buffer de GStreamer.");

        try
        {
            // BGRA es de un solo plano, asi que el tamaño mapeado es exactamente stride * alto.
            var stride = checked((int)(map.Size / (nuint)height));
            var pixels = CopyTightlyPacked(map.Data, stride, width, height);

            return new Frame(pixels, width, height, width * BytesPerPixel, PixelFormat.Bgra8888);
        }
        finally
        {
            gst_buffer_unmap(buffer, ref map);
        }
    }

    // Invariante: todo Frame sale empaquetado, con Stride == Width * BytesPerPixel. GStreamer casi
    // siempre entrega ya empaquetado (camino rapido de una sola copia), pero puede alinear el
    // stride; recompactar aqui evita que el recorte, la deduplicacion y el OCR tengan que pensar
    // en el padding. Ocr.ToSkBitmap, por ejemplo, ignora Stride y saldria una imagen sesgada.
    private static byte[] CopyTightlyPacked(IntPtr source, int stride, int width, int height)
    {
        var rowBytes = width * BytesPerPixel;
        var destination = new byte[rowBytes * height];

        if (stride == rowBytes)
        {
            Marshal.Copy(source, destination, 0, destination.Length);
            return destination;
        }

        for (var row = 0; row < height; row++)
            Marshal.Copy(IntPtr.Add(source, row * stride), destination, row * rowBytes, rowBytes);

        return destination;
    }

    private void ThrowPipelineError(string context)
    {
        var busError = TryReadBusError();

        throw new InvalidOperationException(
            busError is null ? $"{context}." : $"{context}: {busError}");
    }

    private void ThrowIfPipelineFailed()
    {
        if (TryReadBusError() is { } busError)
            throw new InvalidOperationException($"La tuberia de captura fallo: {busError}");
    }

    private string? TryReadBusError()
    {
        if (_pipeline == IntPtr.Zero)
            return null;

        var bus = gst_element_get_bus(_pipeline);

        if (bus == IntPtr.Zero)
            return null;

        try
        {
            var message = gst_bus_pop_filtered(bus, GstMessageError);

            if (message == IntPtr.Zero)
                return null;

            try
            {
                gst_message_parse_error(message, out var error, out var debug);

                var text = ReadGErrorMessage(error);

                if (error != IntPtr.Zero)
                    g_error_free(error);

                if (debug != IntPtr.Zero)
                    g_free(debug);

                return text;
            }
            finally
            {
                gst_mini_object_unref(message);
            }
        }
        finally
        {
            gst_object_unref(bus);
        }
    }

    private void TearDownPipeline()
    {
        if (_appSink != IntPtr.Zero)
        {
            gst_object_unref(_appSink);
            _appSink = IntPtr.Zero;
        }

        if (_pipeline != IntPtr.Zero)
        {
            gst_element_set_state(_pipeline, GstStateNull);
            gst_object_unref(_pipeline);
            _pipeline = IntPtr.Zero;
        }

        _lastFrame = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _gate.WaitAsync();
        try
        {
            TearDownPipeline();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
