using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Entities;

public record Settings(
    InferenceDevice InferenceDevice = InferenceDevice.Cuda,
    OcrEngine OcrEngine = OcrEngine.OnnxRuntime,
    // Tiny debe ser usado exclusivamente para CPU, con Cuda da peores resultados. 
    // Small puede usarse con CPU o Cuda, y es el recomendado para Neni. 
    // Medium es mas preciso pero considerablemente mas lento, y solo se recomiendan con Cuda.
    OcrModelSize OcrModelSize = OcrModelSize.Small,
    int MaxPendingRois = 8,
    int TimerCycleInterval = 650,
    double PreprocessScaleFactor = 1.0,
    double DeduplicationMinEdgeChangedRatio = 0.015,
    double DeduplicationMaxSignatureSide = 120,
    double DeduplicationQuanStep = 3,
    bool CompanionWindowEnabled = false,
    double CompanionWindowOpacity = 0.8,
    Languages SourceLanguage = Languages.English,
    Languages TargetLanguage = Languages.Spanish,
    TranslationModel TranslationModel = TranslationModel.OpusMt);