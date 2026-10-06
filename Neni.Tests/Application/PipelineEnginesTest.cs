using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;
using Neni.Application.Services;

namespace Neni.Tests.Application;

// La carga de motores es todo o nada: un fallo a medias no puede dejar la pipeline marcada como
// lista ni un motor nativo huerfano, y tiene que poder reintentarse.
public class PipelineEnginesTest
{
    [Fact]
    public void Motores_no_se_pueden_usar_antes_de_inicializar()
    {
        var engines = new PipelineEngines(new FakeOcrFactory(), new FakeTranslatorFactory());

        Assert.False(engines.IsReady);
        Assert.Throws<InvalidOperationException>(() => engines.Ocr);
        Assert.Throws<InvalidOperationException>(() => engines.Translator);
    }

    [Fact]
    public async Task Si_falla_el_traductor_se_libera_el_OCR_y_se_puede_reintentar()
    {
        var ocrFactory = new FakeOcrFactory();
        var translatorFactory = new FakeTranslatorFactory { FailNextCreate = true };
        var engines = new PipelineEngines(ocrFactory, translatorFactory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engines.InitializeAsync());

        Assert.False(engines.IsReady);
        Assert.True(ocrFactory.Created[0].Disposed);

        await engines.InitializeAsync();

        Assert.True(engines.IsReady);
        Assert.Same(ocrFactory.Created[1], engines.Ocr);
    }

    [Fact]
    public async Task DisposeAsync_libera_los_dos_motores()
    {
        var ocrFactory = new FakeOcrFactory();
        var translatorFactory = new FakeTranslatorFactory();
        var engines = new PipelineEngines(ocrFactory, translatorFactory);
        await engines.InitializeAsync();

        await engines.DisposeAsync();

        Assert.False(engines.IsReady);
        Assert.True(ocrFactory.Created[0].Disposed);
        Assert.True(translatorFactory.Created[0].Disposed);
    }

    private sealed class FakeOcrFactory : IOcrEngineFactory
    {
        public List<FakeOcr> Created { get; } = [];

        public Task<IOcr> CreateAsync(CancellationToken cancellationToken = default)
        {
            var ocr = new FakeOcr();
            Created.Add(ocr);
            return Task.FromResult<IOcr>(ocr);
        }
    }

    private sealed class FakeTranslatorFactory : ITranslatorEngineFactory
    {
        public bool FailNextCreate { get; set; }
        public List<FakeTranslator> Created { get; } = [];

        public Task<ITranslator> CreateAsync(CancellationToken cancellationToken = default)
        {
            if (FailNextCreate)
            {
                FailNextCreate = false;
                throw new InvalidOperationException("Fallo simulado al crear el traductor.");
            }

            var translator = new FakeTranslator();
            Created.Add(translator);
            return Task.FromResult<ITranslator>(translator);
        }
    }

    private sealed class FakeOcr : IOcr
    {
        public bool Disposed { get; private set; }
        public Task<OcrResult> DetectAsync(Frame frame, CancellationToken cancellationToken = default) => Task.FromResult(OcrResult.Empty);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTranslator : ITranslator
    {
        public bool Disposed { get; private set; }

        public Task<string> TranslateAsync(string text, Languages sourceLanguage, Languages targetLanguage,
            CancellationToken cancellationToken = default) => Task.FromResult(text);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
