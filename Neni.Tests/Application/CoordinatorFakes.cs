using Microsoft.Extensions.Logging.Abstractions;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;
using Neni.Application.Interfaces;
using Neni.Application.Pipeline;
using Neni.Application.Services;

namespace Neni.Tests.Application;

// Dobles minimos para ejercitar el Coordinator sin portal, GStreamer ni modelos. El texto que
// "detecta" el OCR es el ancho del recorte, y el recorte mide lo mismo que la ROI: asi cada reporte
// dice que geometria proceso realmente cada ROI.
internal static class CoordinatorFakes
{
    public static Coordinator CreateCoordinator()
    {
        var (cycleRunner, engines, frameCapture) = CreateCycleRunner();
        return new Coordinator(engines, cycleRunner, frameCapture, new FakeTargetSelector(), NullLogger<Coordinator>.Instance);
    }

    public static (CycleRunner Runner, FakePipelineEngines Engines, FakeFrameCapture FrameCapture) CreateCycleRunner()
    {
        var settings = new Settings(TimerCycleInterval: 10);
        var engines = new FakePipelineEngines();
        var overlay = new FakeOverlay();
        var frameCapture = new FakeFrameCapture();
        var roiProcessor = new RoiProcessor(
            new FakeFrameProcessor(), new FakeDeduplication(), engines, new IdentityNormalizer(), new TranslationCache(engines, settings), settings, NullLogger<RoiProcessor>.Instance);

        return (new CycleRunner(settings, frameCapture, overlay, roiProcessor, new OverlayTracker(overlay)), engines, frameCapture);
    }

    internal sealed class FakePipelineEngines : IPipelineEngines
    {
        public bool IsReady => true;
        public FakeOcr FakeOcr { get; } = new();
        public IOcr Ocr => FakeOcr;
        public ITranslator Translator { get; } = new FakeTranslator();
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class FakeOcr : IOcr
    {
        public int Detections { get; private set; }
        public bool FailNextDetect { get; set; }

        public Task<OcrResult> DetectAsync(Frame frame, CancellationToken cancellationToken = default)
        {
            Detections++;

            if (FailNextDetect)
            {
                FailNextDetect = false;
                throw new InvalidOperationException("Fallo simulado del OCR.");
            }

            TextPoint[] box = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
            return Task.FromResult(new OcrResult([new OcrTextBlock($"{frame.Width}", box, 1f)]));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class IdentityNormalizer : ITextNormalizer
    {
        public string Normalize(string text, Languages sourceLanguage) => text;
    }

    private sealed class FakeTranslator : ITranslator
    {
        public Task<string> TranslateAsync(string text, Languages sourceLanguage, Languages targetLanguage,
            CancellationToken cancellationToken = default) => Task.FromResult(text);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeDeduplication : IDeduplication
    {
        public FrameSignature ComputeSignature(Frame frame) => new([], [], frame.Width, frame.Height);
        public bool IsDuplicate(FrameSignature current, FrameSignature? previous) => false;
    }

    private sealed class FakeFrameProcessor : IFrameProcessor
    {
        public Frame CropFrame(Frame frame, RegionOfInterest roi)
        {
            var width = (int)roi.W;
            return new Frame(new byte[width * 4], width, 1, width * 4, PixelFormat.Bgra8888);
        }

        public Frame ProcessFrame(Frame frame) => frame;
    }

    // Devuelve siempre la misma instancia, como LinuxFrameCapture con una pantalla estatica.
    internal sealed class FakeFrameCapture : IFrameCapture
    {
        private Frame _frame = new(new byte[4], 1, 1, 4, PixelFormat.Bgra8888);

        public bool FailNextGrab { get; set; }

        // Simula que llego un frame nuevo (otra instancia), para saltar el corte por frame completo.
        public void NextFrame() => _frame = _frame with { PixelData = new byte[4] };

        public Task AttachToTargetAsync(CaptureTarget target, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Frame> GrabFrameAsync(CancellationToken cancellationToken = default)
        {
            if (!FailNextGrab)
                return Task.FromResult(_frame);

            FailNextGrab = false;
            throw new InvalidOperationException("Fallo simulado de la captura.");
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeOverlay : IOverlay
    {
        public OverlayCapability CurrentOverlayCapability => OverlayCapability.CompanionWindowOnly;
        public Task InitializeAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task RenderTranslationOverlayAsync(IEnumerable<TranslationOverlayItem> items) => Task.CompletedTask;
        public void UpdateOverlayContent(TranslationOverlayItem item) { }
        public void RemoveOverlayContent(int itemId) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeTargetSelector : ICaptureTargetSelector
    {
        public TargetSelectionMode SelectionMode => TargetSelectionMode.NativePrompt;
        public Task<IEnumerable<CaptureTarget>> ListAvailableTargetsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CaptureTarget?> PromptTargetSelectionAsync(bool reuseLastSelection = false, CancellationToken cancellationToken = default) => Task.FromResult<CaptureTarget?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
