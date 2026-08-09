using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using SkiaSharp;

namespace Neni.Tests.Ocr.Helpers;

public static class TestFrameLoader
{
    public static Frame FromPngFile(string path)
    {
        using SKBitmap bitmap = SKBitmap.Decode(path)
                                ?? throw new InvalidOperationException($"No se pudo decodificar el PNG: {path}");
        return FromSkBitmap(bitmap);
    }
    
        public static Frame FromPngBytes(ReadOnlySpan<byte> pngBytes)
    {
        using SKBitmap bitmap = SKBitmap.Decode(pngBytes)
            ?? throw new InvalidOperationException("No se pudo decodificar el PNG desde el buffer.");

        return FromSkBitmap(bitmap);
    }

    private static Frame FromSkBitmap(SKBitmap bitmap)
    {
        // Frame solo soporta Bgra8888/Rgba8888 (ver PixelFormat). Si el PNG decodifica
        // a otro ColorType (ej. paletizado, escala de grises), lo normalizamos primero.
        SKBitmap normalized = bitmap;
        bool ownsNormalized = false;

        if (bitmap.ColorType is not (SKColorType.Bgra8888 or SKColorType.Rgba8888))
        {
            var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            normalized = new SKBitmap(info);
            ownsNormalized = true;

            using var canvas = new SKCanvas(normalized);
            canvas.DrawBitmap(bitmap, 0, 0);
        }

        try
        {
            PixelFormat format = normalized.ColorType switch
            {
                SKColorType.Bgra8888 => PixelFormat.Bgra8888,
                SKColorType.Rgba8888 => PixelFormat.Rgba8888,
                _ => throw new NotSupportedException(
                    $"ColorType '{normalized.ColorType}' no soportado tras normalizacion.")
            };

            // .Bytes copia el buffer de pixeles a un array administrado.
            // Para PNGs de test (tamano pequeno/moderado) el costo de la copia es irrelevante;
            // en el pipeline real de captura se evita esta copia extra usando el buffer nativo directo.
            byte[] pixelData = normalized.Bytes;

            return new Frame(
                PixelData: pixelData,
                Width: normalized.Width,
                Height: normalized.Height,
                Stride: normalized.RowBytes,
                Format: format);
        }
        finally
        {
            if (ownsNormalized)
            {
                normalized.Dispose();
            }
        }
    }
}