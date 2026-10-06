using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Entities;

// Configuracion completa que ISettings carga y guarda. Cada seccion se registra por separado en el
// contenedor para que cada servicio reciba solo la suya y no pueda depender de las perillas de otra capa.
public sealed record Settings
{
    public CycleSettings Cycle { get; init; } = new();
    public RoiSettings Roi { get; init; } = new();
    public OcrSettings Ocr { get; init; } = new();
    public PreprocessingSettings Preprocessing { get; init; } = new();
    public DeduplicationSettings Deduplication { get; init; } = new();
    public TranslationSettings Translation { get; init; } = new();
    public CompanionWindowSettings CompanionWindow { get; init; } = new();
}

// Ritmo del ciclo de captura (ms entre vueltas).
public sealed record CycleSettings(int TimerCycleInterval = 650);

public sealed record RoiSettings(int MaxPendingRois = 8);

public sealed record OcrSettings(
    InferenceDevice InferenceDevice = InferenceDevice.Cuda,
    OcrEngine Engine = OcrEngine.OnnxRuntime,
    // Tiny debe ser usado exclusivamente para CPU, con Cuda da peores resultados.
    // Small puede usarse con CPU o Cuda, y es el recomendado para Neni.
    // Medium es mas preciso pero considerablemente mas lento, y solo se recomiendan con Cuda.
    OcrModelSize ModelSize = OcrModelSize.Small);

public sealed record PreprocessingSettings(double ScaleFactor = 1.0);

public sealed record DeduplicationSettings(
    // Fraccion minima de pixeles de borde que deben cambiar para considerar que la ROI cambio.
    double MinEdgeChangedRatio = 0.015,
    // Lado maximo al que se reduce la ROI para calcular su firma.
    int MaxSignatureSide = 120,
    // Bits que se descartan del gris antes de comparar, para ignorar ruido de compresion/dithering.
    int QuantStep = 3);

public sealed record TranslationSettings(
    Languages SourceLanguage = Languages.English,
    Languages TargetLanguage = Languages.Spanish,
    TranslationModel Model = TranslationModel.OpusMt);

public sealed record CompanionWindowSettings(bool Enabled = false, double Opacity = 0.8);
