using Neni.Abstractions.Interfaces;
using Neni.Application.Interfaces;

namespace Neni.Application.Services;

// Crea una sola vez por ejecucion el motor de OCR y el traductor a traves de sus factories, y es su
// dueño: el contenedor no los crea, asi que los libera esta clase.
internal sealed class PipelineEngines : IPipelineEngines
{
    private readonly IOcrEngineFactory _ocrEngineFactory;
    private readonly ITranslatorEngineFactory _translatorEngineFactory;
    private IOcr? _ocr;
    private ITranslator? _translator;

    public PipelineEngines(IOcrEngineFactory ocrEngineFactory, ITranslatorEngineFactory translatorEngineFactory)
    {
        _ocrEngineFactory = ocrEngineFactory;
        _translatorEngineFactory = translatorEngineFactory;
    }

    public bool IsReady { get; private set; }

    public IOcr Ocr => IsReady ? _ocr! : throw NotReady();

    public ITranslator Translator => IsReady ? _translator! : throw NotReady();

    /// <summary>
    /// Carga el motor de OCR y el traductor. Todo o nada: solo se marca IsReady si las dos piezas se
    /// crearon bien, asi un fallo (p. ej. sin red en la primera descarga) se puede reintentar.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var ocr = await _ocrEngineFactory.CreateAsync(cancellationToken);

        ITranslator translator;
        try
        {
            translator = await _translatorEngineFactory.CreateAsync(cancellationToken);
        }
        catch
        {
            // Sin esto la sesion nativa del motor quedaria huerfana hasta el cierre del proceso.
            await ocr.DisposeAsync();
            throw;
        }

        _ocr = ocr;
        _translator = translator;
        IsReady = true;
    }

    /// <summary>Libera los dos motores; no hace nada si nunca se inicializaron.</summary>
    public async ValueTask DisposeAsync()
    {
        IsReady = false;

        if (_ocr is not null)
            await _ocr.DisposeAsync();

        if (_translator is not null)
            await _translator.DisposeAsync();

        _ocr = null;
        _translator = null;
    }

    private static InvalidOperationException NotReady()
        => new("Los motores de la pipeline no estan listos: InitializeAsync() debe terminar antes de usarlos.");
}
