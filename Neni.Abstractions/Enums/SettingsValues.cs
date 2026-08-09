namespace Neni.Abstractions.Enums;

public enum InferenceDevice
{
    Cpu = 1,
    Cuda = 2,
    Npu = 3
}

public enum TranslationModel
{
    OpusMt = 1,
    NMT = 2,
    Qwen = 3,
}

public enum Languages
{
    English = 1,
    Japanese = 2,
    Chinese = 3,
    Spanish = 4,
}

public enum OcrEngine
{
    OnnxRuntime = 1,
    OpenVino = 2,
    Paddle = 3
}