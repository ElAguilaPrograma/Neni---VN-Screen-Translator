using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Entities;

public record Settings(
    InferenceDevice InferenceDevice = InferenceDevice.Cpu,
    OcrEngine OcrEngine = OcrEngine.OnnxRuntime,
    OcrModelSize OcrModelSize = OcrModelSize.Tiny,
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