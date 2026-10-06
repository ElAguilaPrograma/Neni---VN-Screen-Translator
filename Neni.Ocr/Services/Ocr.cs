using System.Globalization;
using Microsoft.ML.OnnxRuntime;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;
using RapidOcrNet;
using SkiaSharp;

namespace Neni.Ocr.Services;

internal sealed class Ocr : IOcr
{
    // TODO Instanciar el OCR al iniciar la aplicación.
    private readonly RapidOcrNet.RapidOcr _engine;
    private readonly RapidOcrOptions _options;
    
    // Contrustor privado que solo se llama desde CreateAsync, asi cuando existe una instacia ya esta lista para usarse.
    private Ocr(RapidOcrNet.RapidOcr engine, RapidOcrOptions options)
    {
        _engine = engine;
        _options = options;
    }
    
    // Crea y carga un motor RapidOcr. Resuelve los modelos de PP-OCRv6 del tamaño indicado antes de devolver la instancia
    public static async Task<Ocr> CreateAsync(
        RapidOcrModelManagerService modelManager,
        Settings appSettings,
        OcrModelSize modelSize = OcrModelSize.Tiny,
        RapidOcrOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        RapidOcrModelPaths paths = await modelManager.DetectOcrModelAsync(modelSize, cancellationToken);

        var nativeEngine = new RapidOcrNet.RapidOcr();

        // InitModels es sincrono y pesado, se despacha a un thread pool para no bloquear el caller
        await Task.Run(() =>
        {
            using SessionOptions sessionOptions = ConfigureInferenceSession(appSettings.InferenceDevice, out var actualDevice);
            Console.WriteLine($"Using inference device: {actualDevice}");
            // Hay que pasar sessionOptions al overload que la acepta explicitamente:
            // el overload sin SessionOptions ignora silenciosamente la config de CUDA/CPU
            // construida arriba y siempre inicializa con el ejecutor CPU por defecto.
            nativeEngine.InitModels(
                detPath: paths.DetectionModelPath,
                clsPath: paths.ClassificationModelPath,
                recPath: paths.RecognitionModelPath,
                keysPath: paths.DictionaryPath,
                op: sessionOptions);
        }, cancellationToken);

        // El preset RapidOcrOptions.PPOCRv6 (recomendado por el README de RapidOcrNet para v6) usa
        // resize adaptativo por el lado CORTO (LimitSideLen=736, con ImgResize=0), pensado para fotos
        // donde la imagen de entrada puede ser mas chica que lo que el detector espera. Los ROIs de
        // Neni son al reves: recortes anchos y bajos (ej. 1413x148, ratio ~9.5:1) tomados directo de
        // pantalla. Con WidthHeightRatio=8 eso dispara el letterbox vertical y despues el resize
        // adaptativo escala el lado corto (ya inflado por el letterbox) hasta 736px, mandando al
        // detector una imagen ~16x mas grande en pixeles en cada frame — medido: 251ms (v5+Default)
        // vs 1971ms (v6+PPOCRv6) vs 349ms (v6+PPOCRv6 con ImgResize=1024) sobre las mismas 10 imagenes.
        // Se mantiene el resto del preset v6 (sin borde blanco, letterbox) pero se reimpone el recorte
        // por lado largo de Default para que el detector nunca upscalee un recorte que ya viene a
        // resolucion de pantalla.
        // TODO: DoAngle = false es un hack para que el detector no gire los ROIs de Neni, que son siempre horizontales. 
        // Revisar si el modelo de clasificación es necesario o puede ser omitido para acelerar la descarga inicial y el InitModels.
        return new Ocr(nativeEngine, options ?? RapidOcrOptions.PPOCRv6 with { DoAngle = false, ImgResize = 1024 });
    }

    public async Task<Neni.Abstractions.Entities.OcrResult> DetectAsync(Frame frame,
        CancellationToken cancellationToken = default)
    {
        using SKBitmap bitmap = ToSkBitmap(frame);
        
        // Rapid.Ocr es sincrono, Task.Run evita bloquear el hilo que lo llama IMPORTANTE PORQUE SI NO EL LOOP QUE LO LLAMA SE ROMPE
        RapidOcrNet.OcrResult native = await Task.Run(
            () => _engine.Detect(bitmap, _options),
            cancellationToken);

        return MapToAbstraction(native);
    }

    public string NormalizeText(string text, Languages sourceLanguage) =>
        TextNormalizer.Normalize(text, TextNormalizer.ScriptFor(sourceLanguage));

    public async ValueTask DisposeAsync()
    {
        _engine.Dispose();
        await Task.CompletedTask;
    }

    private static SessionOptions ConfigureInferenceSession(InferenceDevice device, out InferenceDevice actualDevice)
    {
        var sessionOptions = RapidOcr.GetDefaultSessionOptions();
        actualDevice = InferenceDevice.Cpu;

        if (device == InferenceDevice.Cuda)
        {
            try
            {
                Console.WriteLine("Attempting to use CUDA execution provider for ONNX Runtime.");
                sessionOptions.AppendExecutionProvider_CUDA();
                actualDevice = InferenceDevice.Cuda;
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException)
            {
                // Log warning: CUDA no esta disponible, se usara CPU.
                Console.WriteLine($"Warning: CUDA no esta disponible, se usara CPU. Exception: {ex.Message}");
            }
        }
        return sessionOptions;
    }

    // Convertir el frame en un SKBitmap que es el formato que espera RapidOcr
    private static SKBitmap ToSkBitmap(Frame frame)
    {
        var colorType = frame.Format switch
        {
            PixelFormat.Bgra8888 => SKColorType.Bgra8888,
            PixelFormat.Rgba8888 => SKColorType.Rgba8888,
            _ => throw new NotSupportedException($"Formato no soportado: {frame.Format}")
        };

        var info = new SKImageInfo(frame.Width, frame.Height, colorType, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);

        frame.PixelData.Span.CopyTo(bitmap.GetPixelSpan());

        return bitmap;
    }
    
    // Mapear salida del Ocr a la abstracción OcrResult
    private static Neni.Abstractions.Entities.OcrResult MapToAbstraction(RapidOcrNet.OcrResult native)
    {
        var blocks = native.TextBlocks.Select(b => new OcrTextBlock(
            Text: b.Text,
            BoxPoints: b.BoxPoints.Select(p => new TextPoint(p.X, p.Y)).ToArray(),
            Confidence: b.CharScores is { Length: > 0 } scores ? scores.Average() : 0f
        )).ToList();

        return new Neni.Abstractions.Entities.OcrResult(blocks);
    }
}