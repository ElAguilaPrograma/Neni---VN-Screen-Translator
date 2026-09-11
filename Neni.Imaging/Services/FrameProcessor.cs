using System.Runtime.InteropServices;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;
using SkiaSharp;

namespace Neni.Imaging.Services;

public sealed class FrameProcessor : IFrameProcessor
{
    private const int BytesPerPixel = 4;

    // Recorta la ROI y le aplica su propio factor de escala: CropFrame es el unico punto del
    // pipeline que recibe la ROI, asi que roi.Scale no puede aplicarse mas adelante.
    public Frame CropFrame(Frame frame, RegionOfInterest roi)
    {
        if (roi.Scale <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(roi), roi.Scale, $"La escala de la ROI #{roi.RoiId} debe ser mayor a 0.");

        var colorType = ToSkColorType(frame.Format);

        // Los limites se redondean hacia afuera para no cortar el borde de un glifo, y se recortan
        // contra el frame la ROI
        var left = Math.Clamp((int)Math.Floor(roi.X), 0, frame.Width);
        var top = Math.Clamp((int)Math.Floor(roi.Y), 0, frame.Height);
        var right = Math.Clamp((int)Math.Ceiling(roi.X + roi.W), left, frame.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(roi.Y + roi.H), top, frame.Height);

        var sourceWidth = right - left;
        var sourceHeight = bottom - top;

        if (sourceWidth == 0 || sourceHeight == 0)
            throw new ArgumentException(
                $"La ROI #{roi.RoiId} ({roi.X},{roi.Y} {roi.W}x{roi.H}) no intersecta el frame de {frame.Width}x{frame.Height}.",
                nameof(roi));

        if (!MemoryMarshal.TryGetArray(frame.PixelData, out var source) || source.Array is null)
            throw new InvalidOperationException("Frame.PixelData no está respaldado por un array administrado.");

        var destWidth = Math.Max(1, (int)Math.Round(sourceWidth * roi.Scale));
        var destHeight = Math.Max(1, (int)Math.Round(sourceHeight * roi.Scale));

        // El recorte sale empaquetado (Stride == Width * BytesPerPixel) porque Ocr.ToSkBitmap copia
        // plano ignorando Stride con padding la imagen le llegaria sesgada al detector.
        var destStride = destWidth * BytesPerPixel;
        var destPixels = new byte[destStride * destHeight];

        // Alfa Opaque en origen y destino el contenido de pantalla es opaco pero el canal alfa que
        // entrega el compositor no siempre lo refleja, y declararlo Premul dejaria que ese byte
        // apagara el color real que tiene que leer el OCR.
        var sourceInfo = new SKImageInfo(frame.Width, frame.Height, colorType, SKAlphaType.Opaque);
        var destInfo = new SKImageInfo(destWidth, destHeight, colorType, SKAlphaType.Opaque);

        // Sin reescalado la traslacion es entera y cada pixel destino cae exactamente sobre uno de
        // origen, asi que Nearest devuelve el recorte identico al original; el remuestreo con
        // Mitchell solo entra cuando Scale != 1.
        var sampling = destWidth == sourceWidth && destHeight == sourceHeight
            ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);

        var sourceHandle = GCHandle.Alloc(source.Array, GCHandleType.Pinned);
        var destHandle = GCHandle.Alloc(destPixels, GCHandleType.Pinned);

        try
        {
            using var sourceImage = SKImage.FromPixels(
                sourceInfo, sourceHandle.AddrOfPinnedObject() + source.Offset, frame.Stride);
            using var surface = SKSurface.Create(destInfo, destHandle.AddrOfPinnedObject(), destStride);

            surface.Canvas.DrawImage(
                sourceImage,
                SKRect.Create(left, top, sourceWidth, sourceHeight),
                SKRect.Create(destWidth, destHeight),
                sampling,
                paint: null);
        }
        finally
        {
            destHandle.Free();
            sourceHandle.Free();
        }

        return new Frame(destPixels, destWidth, destHeight, destStride, frame.Format);
    }

    // TODO pendiente: definir que preprocesado ayuda de verdad a la deteccion (binarizado, contraste,
    // escala global de Settings.PreprocessScaleFactor). Hasta entonces falla explicito en vez de
    // devolver el frame intacto y aparentar que hace algo.
    public Frame ProcessFrame(Frame frame)
        => throw new NotImplementedException();

    private static SKColorType ToSkColorType(PixelFormat format) => format switch
    {
        PixelFormat.Bgra8888 => SKColorType.Bgra8888,
        PixelFormat.Rgba8888 => SKColorType.Rgba8888,
        _ => throw new NotSupportedException($"Formato no soportado: {format}")
    };
}
