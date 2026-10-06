using Neni.Abstractions.Entities;
using Neni.Abstractions.Enums;
using Neni.Abstractions.Interfaces;
using Neni.Application.Interfaces;
using Neni.Application.Pipeline;

namespace Neni.Tests.Application;

public class TranslationCacheTest
{
    [Fact]
    public async Task Misma_linea_se_traduce_una_sola_vez()
    {
        var engines = new CountingEngines();
        var cache = new TranslationCache(engines, new TranslationSettings());

        await cache.TranslateAsync("hola");
        await cache.TranslateAsync("hola");

        Assert.Equal(1, engines.Translations);
    }

    [Fact]
    public async Task Al_llenarse_desaloja_la_linea_usada_hace_mas_tiempo()
    {
        var engines = new CountingEngines();
        var cache = new TranslationCache(engines, new TranslationSettings(), capacity: 2);

        await cache.TranslateAsync("a");
        await cache.TranslateAsync("b");
        await cache.TranslateAsync("a");  // "a" pasa a ser la mas reciente
        await cache.TranslateAsync("c");  // desaloja "b"

        Assert.Equal(2, cache.Count);
        await cache.TranslateAsync("a");
        Assert.Equal(3, engines.Translations);
        await cache.TranslateAsync("b");
        Assert.Equal(4, engines.Translations);
    }

    private sealed class CountingEngines : IPipelineEngines, ITranslator
    {
        public int Translations { get; private set; }
        public bool IsReady => true;
        public IOcr Ocr => throw new NotSupportedException();
        public ITranslator Translator => this;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> TranslateAsync(string text, Languages sourceLanguage, Languages targetLanguage,
            CancellationToken cancellationToken = default)
        {
            Translations++;
            return Task.FromResult($"[{text}]");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
