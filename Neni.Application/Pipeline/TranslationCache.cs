using Neni.Abstractions.Entities;
using Neni.Application.Interfaces;

namespace Neni.Application.Pipeline;

// Cache LRU acotado delante del traductor: el OCR relee la misma linea en cada vuelta mientras el
// dialogo sigue en pantalla, y traducir es lo caro. Acotado porque una sesion larga de VN produce
// miles de lineas distintas que nunca vuelven a aparecer. Solo lo usa el hilo del ciclo.
internal sealed class TranslationCache
{
    public const int DefaultCapacity = 512;

    private readonly IPipelineEngines _engines;
    private readonly Settings _settings;
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<(string Source, string Translated)>> _entries = new();
    // Mas reciente al frente; se desaloja desde el final.
    private readonly LinkedList<(string Source, string Translated)> _recency = new();

    public TranslationCache(IPipelineEngines engines, Settings settings, int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _engines = engines;
        _settings = settings;
        _capacity = capacity;
    }

    public int Count => _entries.Count;

    /// <summary>Devuelve la traduccion cacheada o la pide al traductor y la guarda.</summary>
    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(text, out var node))
        {
            _recency.Remove(node);
            _recency.AddFirst(node);
            return node.Value.Translated;
        }

        var translated = await _engines.Translator.TranslateAsync(
            text, _settings.SourceLanguage, _settings.TargetLanguage, cancellationToken);

        if (_entries.Count >= _capacity)
        {
            var oldest = _recency.Last!;
            _recency.RemoveLast();
            _entries.Remove(oldest.Value.Source);
        }

        _entries[text] = _recency.AddFirst((text, translated));
        return translated;
    }
}
