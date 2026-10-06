using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;

namespace Neni.Imaging.Services;

internal sealed class FrameProcessor : IFrameProcessor
{
    public Frame CropFrame(Frame frame, RegionOfInterest roi)
    {
        var bytesPerPixel = BytesPerPixelFor(frame.Format);

        // Los limites se redondean hacia afuera para no cortar el borde de un glifo, y se recortan
        // contra el frame la ROI
        var left = Math.Clamp((int)Math.Floor(roi.X), 0, frame.Width);
        var top = Math.Clamp((int)Math.Floor(roi.Y), 0, frame.Height);
        var right = Math.Clamp((int)Math.Ceiling(roi.X + roi.W), left, frame.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(roi.Y + roi.H), top, frame.Height);

        var width = right - left;
        var height = bottom - top;

        if (width == 0 || height == 0)
            throw new ArgumentException(
                $"La ROI #{roi.RoiId} ({roi.X},{roi.Y} {roi.W}x{roi.H}) no intersecta el frame de {frame.Width}x{frame.Height}.",
                nameof(roi));

        // El recorte sale empaquetado (Stride == Width * bytesPerPixel) porque Ocr.ToSkBitmap copia
        // plano ignorando Stride con padding la imagen le llegaria sesgada al detector.
        var stride = width * bytesPerPixel;
        var pixels = new byte[stride * height];
        var source = frame.PixelData.Span;

        for (var row = 0; row < height; row++)
            source.Slice((top + row) * frame.Stride + left * bytesPerPixel, stride)
                .CopyTo(pixels.AsSpan(row * stride));

        return new Frame(pixels, width, height, stride, frame.Format);
    }

    // TODO pendiente: definir que preprocesado ayuda de verdad a la deteccion (binarizado, contraste,
    // escala global de Settings.PreprocessScaleFactor). Hasta entonces devuelve el recorte intacto,
    // igual que el stub de Ocr.NormalizeText.
    public Frame ProcessFrame(Frame frame) => frame;

    private static int BytesPerPixelFor(PixelFormat format) => format switch
    {
        PixelFormat.Bgra8888 or PixelFormat.Rgba8888 => 4,
        _ => throw new NotSupportedException($"Formato no soportado: {format}")
    };
}
