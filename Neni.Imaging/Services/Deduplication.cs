using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;

namespace Neni.Imaging.Services;

internal sealed class Deduplication : IDeduplication
{
    // No se exponen en Settings porque MinEdgeChangedRatio se
    // calibro justamente contra la densidad de bordes que produce Canny con estos dos valores.
    private const int CannyLowThreshold = 100;
    private const int CannyHighThreshold = 200;

    private readonly double _minEdgeChangedRatio;
    private readonly int _maxSignatureSide;
    private readonly int _quantStep;

    public Deduplication(DeduplicationSettings settings)
    {
        _minEdgeChangedRatio = settings.MinEdgeChangedRatio;
        _maxSignatureSide = settings.MaxSignatureSide;
        _quantStep = settings.QuantStep;
    }

    public FrameSignature ComputeSignature(Frame frame)
    {
        var (gray, width, height) = ToDownsampledGray(frame);

        var quantized = new byte[gray.Length];
        for (var i = 0; i < gray.Length; i++)
            quantized[i] = (byte)(gray[i] >> _quantStep);

        // Los bordes se calculan sobre el gris SIN cuantizar, cuantizar es solo para la comparacion
        // exacta de la primera etapa.
        return new EdgeFrameSignature(quantized, DetectEdges(gray, width, height), width, height);
    }

    public bool IsDuplicate(FrameSignature currentSignature, FrameSignature? previousSignature)
    {
        // Primer frame de esta ROI, no hay contra que comparar, hay que correr el OCR.
        if (previousSignature is null)
            return false;

        var current = AsEdgeSignature(currentSignature, nameof(currentSignature));
        var previous = AsEdgeSignature(previousSignature, nameof(previousSignature));

        // La ROI cambio de tamaño, las firmas no son comparables, se trata como cambio.
        if (current.Width != previous.Width || current.Height != previous.Height)
            return false;

        // Etapa 1: no cambio ni un nivel de gris relevante.
        if (current.QuantizedGray.AsSpan().SequenceEqual(previous.QuantizedGray))
            return true;

        // Etapa 2: algo cambio, pero solo cuenta si movio la estructura lo suficiente.
        var changed = 0;
        for (var i = 0; i < current.Edges.Length; i++)
            if (current.Edges[i] != previous.Edges[i])
                changed++;

        return changed / (double)current.Edges.Length < _minEdgeChangedRatio;
    }

    // Gris + reduccion promediando por area, cada pixel destino
    // promedia todos los de origen que cubre, con peso fraccional en los bordes del bloque. Ambas
    // operaciones son lineales, asi que fusionarlas da exactamente lo mismo que convertir primero a
    // gris y reducir despues, en una sola pasada.
    private (byte[] Gray, int Width, int Height) ToDownsampledGray(Frame frame)
    {
        var (redOffset, blueOffset) = frame.Format switch
        {
            PixelFormat.Bgra8888 => (2, 0),
            PixelFormat.Rgba8888 => (0, 2),
            _ => throw new NotSupportedException($"Formato no soportado: {frame.Format}")
        };

        // Solo reduce, nunca amplia.
        var scale = Math.Min(1.0, _maxSignatureSide / (double)Math.Max(frame.Width, frame.Height));
        var width = Math.Max(1, (int)(frame.Width * scale));
        var height = Math.Max(1, (int)(frame.Height * scale));

        var gray = new byte[width * height];
        var source = frame.PixelData.Span;
        var boxWidth = frame.Width / (double)width;
        var boxHeight = frame.Height / (double)height;

        for (var destY = 0; destY < height; destY++)
        {
            var top = destY * boxHeight;
            var bottom = top + boxHeight;
            var lastRow = Math.Min(frame.Height - 1, (int)Math.Ceiling(bottom) - 1);

            for (var destX = 0; destX < width; destX++)
            {
                var left = destX * boxWidth;
                var right = left + boxWidth;
                var lastColumn = Math.Min(frame.Width - 1, (int)Math.Ceiling(right) - 1);

                double sum = 0;
                double totalWeight = 0;

                for (var sourceY = (int)top; sourceY <= lastRow; sourceY++)
                {
                    var weightY = Math.Min(bottom, sourceY + 1) - Math.Max(top, sourceY);
                    if (weightY <= 0)
                        continue;

                    var rowStart = sourceY * frame.Stride;

                    for (var sourceX = (int)left; sourceX <= lastColumn; sourceX++)
                    {
                        var weightX = Math.Min(right, sourceX + 1) - Math.Max(left, sourceX);
                        if (weightX <= 0)
                            continue;

                        var pixel = rowStart + sourceX * 4;

                        // Mismos coeficientes que COLOR_BGR2GRAY de OpenCV.
                        var luma = 0.299 * source[pixel + redOffset]
                                 + 0.587 * source[pixel + 1]
                                 + 0.114 * source[pixel + blueOffset];

                        var weight = weightX * weightY;
                        sum += luma * weight;
                        totalWeight += weight;
                    }
                }

                gray[destY * width + destX] = (byte)Math.Clamp(Math.Round(sum / totalWeight), 0, 255);
            }
        }

        return (gray, width, height);
    }

    private static byte[] DetectEdges(byte[] gray, int width, int height)
    {
        var gradientX = new int[gray.Length];
        var gradientY = new int[gray.Length];
        var magnitude = new int[gray.Length];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int Sample(int offsetX, int offsetY)
                    => gray[Math.Clamp(y + offsetY, 0, height - 1) * width + Math.Clamp(x + offsetX, 0, width - 1)];

                var gx = -Sample(-1, -1) + Sample(1, -1)
                         - 2 * Sample(-1, 0) + 2 * Sample(1, 0)
                         - Sample(-1, 1) + Sample(1, 1);

                var gy = -Sample(-1, -1) - 2 * Sample(0, -1) - Sample(1, -1)
                         + Sample(-1, 1) + 2 * Sample(0, 1) + Sample(1, 1);

                var index = y * width + x;
                gradientX[index] = gx;
                gradientY[index] = gy;
                magnitude[index] = Math.Abs(gx) + Math.Abs(gy);
            }
        }

        var survivors = NonMaximumSuppression(gradientX, gradientY, magnitude, width, height);

        return TrackEdgesByHysteresis(survivors, width, height);
    }

    private static int[] NonMaximumSuppression(int[] gradientX, int[] gradientY, int[] magnitude, int width, int height)
    {
        const double Tangent22 = 0.41421356; // tan(22.5°)
        const double Tangent67 = 2.41421356; // tan(67.5°)

        var survivors = new int[magnitude.Length];

        int MagnitudeAt(int x, int y)
            => x < 0 || y < 0 || x >= width || y >= height ? 0 : magnitude[y * width + x];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var current = magnitude[index];

                if (current < CannyLowThreshold)
                    continue;

                var absoluteX = Math.Abs(gradientX[index]);
                var absoluteY = Math.Abs(gradientY[index]);

                int stepX;
                int stepY;

                if (absoluteY <= Tangent22 * absoluteX)
                {
                    stepX = 1;
                    stepY = 0;
                }
                else if (absoluteY >= Tangent67 * absoluteX)
                {
                    stepX = 0;
                    stepY = 1;
                }
                else
                {
                    // Signos iguales -> diagonal principal; signos opuestos -> antidiagonal.
                    stepX = 1;
                    stepY = (gradientX[index] ^ gradientY[index]) >= 0 ? 1 : -1;
                }

                if (current < MagnitudeAt(x - stepX, y - stepY) || current < MagnitudeAt(x + stepX, y + stepY))
                    continue;

                survivors[index] = current;
            }
        }

        return survivors;
    }

    // Doble umbral: los pixeles fuertes son borde seguro, y los debiles solo si estan conectados
    // (8-vecinos) a alguno fuerte. Devuelve la mascara 0/1.
    private static byte[] TrackEdgesByHysteresis(int[] survivors, int width, int height)
    {
        var edges = new byte[survivors.Length];
        var pending = new Stack<int>();

        for (var index = 0; index < survivors.Length; index++)
        {
            if (survivors[index] < CannyHighThreshold)
                continue;

            edges[index] = 1;
            pending.Push(index);
        }

        while (pending.Count > 0)
        {
            var index = pending.Pop();
            var x = index % width;
            var y = index / width;

            for (var offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (var offsetX = -1; offsetX <= 1; offsetX++)
                {
                    var neighborX = x + offsetX;
                    var neighborY = y + offsetY;

                    if (neighborX < 0 || neighborY < 0 || neighborX >= width || neighborY >= height)
                        continue;

                    var neighbor = neighborY * width + neighborX;

                    // survivors vale 0 en lo que la supresion descarto, asi que este mismo check
                    // cubre tanto "no sobrevivio" como "quedo por debajo del umbral bajo".
                    if (edges[neighbor] != 0 || survivors[neighbor] < CannyLowThreshold)
                        continue;

                    edges[neighbor] = 1;
                    pending.Push(neighbor);
                }
            }
        }

        return edges;
    }

    // Las firmas solo se comparan con firmas de esta misma implementacion.
    private static EdgeFrameSignature AsEdgeSignature(FrameSignature signature, string paramName)
        => signature as EdgeFrameSignature
           ?? throw new ArgumentException(
               $"Deduplication solo compara firmas propias, recibio {signature.GetType().Name}.", paramName);
}
