using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;
using Neni.Application.Interfaces;
using Neni.Application.Pipeline;
using Neni.Imaging.Services;
using Neni.Tests.Ocr.Helpers;

namespace Neni.Tests.Application;

// RoiProcessor con el recorte y la deduplicacion reales sobre capturas reales de VN; solo el OCR y
// el traductor son dobles (el OCR real ya tiene su propio test).
public class RoiProcessorTest
{
    private const string FramesDirectory = "Ocr/Frames/English/All";

    private static readonly Frame FirstFrame = TestFrameLoader.FromPngFile(Path.Combine(FramesDirectory, "10.png"));
    private static readonly Frame SecondFrame = TestFrameLoader.FromPngFile(Path.Combine(FramesDirectory, "11.png"));

    // ROI que cabe en las dos capturas, para que las firmas tengan el mismo tamaño y se comparen de verdad.
    private static readonly RegionOfInterest Roi = new(1, 0, 0,
        Math.Min(FirstFrame.Width, SecondFrame.Width),
        Math.Min(FirstFrame.Height, SecondFrame.Height));

    [Fact]
    public async Task Mismo_frame_dos_veces_la_segunda_es_Unchanged()
    {
        var processor = CreateProcessor(new FakeEngines());

        var first = await processor.ProcessAsync(Roi, FirstFrame);
        var second = await processor.ProcessAsync(Roi, FirstFrame);

        Assert.Equal(RoiOutcome.Updated, first.Outcome);
        Assert.Equal("texto detectado", Assert.Single(first.Blocks).TranslatedText);
        Assert.Equal(RoiOutcome.Unchanged, second.Outcome);
    }

    [Fact]
    public async Task Otra_pantalla_de_dialogo_vuelve_a_correr_el_OCR()
    {
        var engines = new FakeEngines();
        var processor = CreateProcessor(engines);

        await processor.ProcessAsync(Roi, FirstFrame);
        var result = await processor.ProcessAsync(Roi, SecondFrame);

        Assert.Equal(RoiOutcome.Updated, result.Outcome);
        Assert.Equal(2, engines.Detections);
    }

    [Fact]
    public async Task Una_ROI_fuera_del_frame_es_Failed_no_Unchanged()
    {
        var processor = CreateProcessor(new FakeEngines());
        var outside = new RegionOfInterest(2, FirstFrame.Width + 10, 0, 50, 50);

        var result = await processor.ProcessAsync(outside, FirstFrame);

        Assert.Equal(RoiOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Si_falla_la_traduccion_la_siguiente_vuelta_reintenta()
    {
        var engines = new FakeEngines { FailNextTranslation = true };
        var processor = CreateProcessor(engines);

        var failed = await processor.ProcessAsync(Roi, FirstFrame);
        var retried = await processor.ProcessAsync(Roi, FirstFrame);

        // Si la firma se hubiera guardado antes de traducir, el reintento saldria Unchanged y esa
        // ROI se quedaria sin texto hasta que cambiara la pantalla.
        Assert.Equal(RoiOutcome.Failed, failed.Outcome);
        Assert.Equal(RoiOutcome.Updated, retried.Outcome);
    }

    private static RoiProcessor CreateProcessor(FakeEngines engines)
    {
        var settings = new Settings();
        return new RoiProcessor(
            new FrameProcessor(), new Deduplication(settings), engines, new TranslationCache(engines, settings), settings);
    }

    private sealed class FakeEngines : IPipelineEngines, IOcr, ITranslator
    {
        public int Detections { get; private set; }
        public bool FailNextTranslation { get; set; }
        public bool IsReady => true;
        public IOcr Ocr => this;
        public ITranslator Translator => this;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<OcrResult> DetectAsync(Frame frame, CancellationToken cancellationToken = default)
        {
            Detections++;
            TextPoint[] box = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
            return Task.FromResult(new OcrResult([new OcrTextBlock("texto detectado", box, 1f)]));
        }

        public string NormalizeText(string text, Languages sourceLanguage) => text;

        public Task<string> TranslateAsync(string text, Languages sourceLanguage, Languages targetLanguage,
            CancellationToken cancellationToken = default)
        {
            if (FailNextTranslation)
            {
                FailNextTranslation = false;
                throw new InvalidOperationException("Fallo simulado del traductor.");
            }

            return Task.FromResult(text);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
