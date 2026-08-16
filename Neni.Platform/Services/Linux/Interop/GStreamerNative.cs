using System.Runtime.InteropServices;

namespace Neni.Platform.Services.Linux.Interop;

// P/Invoke minimo a GStreamer. Solo lo necesario para montar una tuberia con gst_parse_launch
// y sacar buffers por un appsink; toda la negociacion de formatos la hace GStreamer por nosotros.
//
// Se conservan los nombres nativos a proposito para que el mapeo con la documentacion de
// GStreamer sea directo.
internal static partial class GStreamerNative
{
    private const string LibGStreamer = "libgstreamer-1.0.so.0";
    private const string LibGstApp = "libgstapp-1.0.so.0";
    private const string LibGLib = "libglib-2.0.so.0";
    private const string LibC = "libc";

    // GstState
    internal const int GstStateNull = 1;
    internal const int GstStatePlaying = 4;

    // GstStateChangeReturn
    internal const int GstStateChangeFailure = 0;

    // GstMessageType (mascara de bits): EOS = 1 << 0, ERROR = 1 << 1.
    internal const uint GstMessageError = 1 << 1;

    // GstMapFlags
    internal const int GstMapRead = 1;

    // GError = { GQuark domain (4 bytes); gint code (4 bytes); gchar *message; }
    // El puntero al mensaje queda en el offset 8 tanto en 32 como en 64 bits.
    internal const int GErrorMessageOffset = 8;

    [LibraryImport(LibGStreamer)]
    internal static partial void gst_init(IntPtr argc, IntPtr argv);

    [LibraryImport(LibGStreamer, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr gst_parse_launch(string pipelineDescription, out IntPtr error);

    [LibraryImport(LibGStreamer, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr gst_bin_get_by_name(IntPtr bin, string name);

    [LibraryImport(LibGStreamer)]
    internal static partial int gst_element_set_state(IntPtr element, int state);

    [LibraryImport(LibGStreamer)]
    internal static partial int gst_element_get_state(IntPtr element, out int state, out int pending, ulong timeoutNs);

    [LibraryImport(LibGStreamer)]
    internal static partial IntPtr gst_element_get_bus(IntPtr element);

    [LibraryImport(LibGStreamer)]
    internal static partial IntPtr gst_bus_pop_filtered(IntPtr bus, uint messageTypes);

    [LibraryImport(LibGStreamer)]
    internal static partial void gst_message_parse_error(IntPtr message, out IntPtr error, out IntPtr debug);

    [LibraryImport(LibGStreamer)]
    internal static partial void gst_object_unref(IntPtr @object);

    // gst_sample_unref / gst_message_unref / gst_buffer_unref son macros que acaban aqui.
    [LibraryImport(LibGStreamer)]
    internal static partial void gst_mini_object_unref(IntPtr miniObject);

    [LibraryImport(LibGStreamer)]
    internal static partial IntPtr gst_sample_get_buffer(IntPtr sample);

    [LibraryImport(LibGStreamer)]
    internal static partial IntPtr gst_sample_get_caps(IntPtr sample);

    [LibraryImport(LibGStreamer)]
    internal static partial IntPtr gst_caps_get_structure(IntPtr caps, uint index);

    // Devuelven gboolean (gint): 0 es fallo.
    [LibraryImport(LibGStreamer, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int gst_structure_get_int(IntPtr structure, string fieldName, out int value);

    // Devuelve un const gchar* propiedad de la estructura: NO se libera.
    [LibraryImport(LibGStreamer, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr gst_structure_get_string(IntPtr structure, string fieldName);

    [LibraryImport(LibGStreamer)]
    internal static partial int gst_buffer_map(IntPtr buffer, out GstMapInfo info, int flags);

    [LibraryImport(LibGStreamer)]
    internal static partial void gst_buffer_unmap(IntPtr buffer, ref GstMapInfo info);

    // timeoutNs es un GstClockTime en nanosegundos.
    [LibraryImport(LibGstApp)]
    internal static partial IntPtr gst_app_sink_try_pull_sample(IntPtr appSink, ulong timeoutNs);

    [LibraryImport(LibGLib)]
    internal static partial void g_error_free(IntPtr error);

    [LibraryImport(LibGLib)]
    internal static partial void g_free(IntPtr memory);

    // pipewiresrc se queda con la propiedad del fd que le pasamos (lo cierra al destruirse),
    // asi que hay que duplicarlo y darle la copia; el original lo sigue gestionando el portal.
    [LibraryImport(LibC, SetLastError = true)]
    internal static partial int dup(int fileDescriptor);

    internal static string ReadGErrorMessage(IntPtr error)
    {
        if (error == IntPtr.Zero)
            return "error desconocido";

        var messagePointer = Marshal.ReadIntPtr(error, GErrorMessageOffset);
        return Marshal.PtrToStringUTF8(messagePointer) ?? "error desconocido";
    }
}

// struct GstMapInfo { GstMemory *memory; GstMapFlags flags; guint8 *data; gsize size;
//                     gsize maxsize; gpointer user_data[4]; gpointer _gst_reserved[4]; }
// Los campos reservados tienen que estar declarados: gst_buffer_map escribe la estructura entera.
[StructLayout(LayoutKind.Sequential)]
internal struct GstMapInfo
{
    internal IntPtr Memory;
    internal int Flags;
    internal IntPtr Data;
    internal nuint Size;
    internal nuint MaxSize;

    private IntPtr _userData0;
    private IntPtr _userData1;
    private IntPtr _userData2;
    private IntPtr _userData3;
    private IntPtr _reserved0;
    private IntPtr _reserved1;
    private IntPtr _reserved2;
    private IntPtr _reserved3;
}
