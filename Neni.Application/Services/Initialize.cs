using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;
using Neni.Application.Interfaces;

namespace Neni.Application.Services;

// Inicializa lo pesado que la pipeline necesita (modelo de OCR, modelo de traduccion) a partir de
// las settings que ya cargo el contenedor. Se hace una sola vez por ejecucion.

internal sealed class Initialize : IInitialize
{
    private readonly IOcrEngineFactory _ocrEngineFactory;
    private readonly ITranslatorEngineFactory _translatorEngineFactory;

    public bool IsInitialized { get; private set; }
    public Settings AppSettings { get; }
    public IOcr Engine { get; private set; } = null!;
    public ITranslator Translator { get; private set; } = null!;

    public Initialize(IOcrEngineFactory ocrEngineFactory,
        ITranslatorEngineFactory translatorEngineFactory,
        Settings settings)
    {
        _ocrEngineFactory = ocrEngineFactory;
        _translatorEngineFactory = translatorEngineFactory;
        AppSettings = settings;
    }

    /// <summary>
    /// Carga el motor de OCR y el traductor. Todo o nada: solo se marca IsInitialized si las dos
    /// piezas se crearon bien, porque Coordinator lo usa como bandera de "listo".
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var engine = await _ocrEngineFactory.CreateAsync(cancellationToken);

        ITranslator translator;
        try
        {
            translator = await _translatorEngineFactory.CreateAsync(cancellationToken);
        }
        catch
        {
            // Sin esto la sesion nativa del motor quedaria huerfana hasta el cierre del proceso.
            await engine.DisposeAsync();
            throw;
        }

        Engine = engine;
        Translator = translator;
        IsInitialized = true;
    }

    /// <summary>
    /// Libera el motor de OCR y el traductor. Tolera una inicializacion a medias: si la creacion
    /// del motor fallo, Engine o Translator pueden no estar asignados.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Engine is not null)
            await Engine.DisposeAsync();

        if (Translator is not null)
            await Translator.DisposeAsync();
    }
}
