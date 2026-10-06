using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;
using Neni.Application.Interfaces;

namespace Neni.Application.Services;

// Aqui se debe de inicializar todo lo que sea necesario para que la aplicacion funcione,
// primero se deben inicializar las settings, luego los servicios que dependen de esas settings,
// como el modelo de OCR, el modelo de traducción, etc. Se debe de hacer una sola vez al inicio de la aplicacion.

internal sealed class Initialize : IInitialize
{
    private readonly ISettings _settings;
    private readonly IOcrEngineFactory _ocrEngineFactory;
    private readonly ITranslatorEngineFactory _translatorEngineFactory;

    public Settings AppSettings { get; private set; } = null!;
    public IOcr Engine { get; private set; } = null!;
    public ITranslator Translator { get; private set; } = null!;

    public Initialize(IOcrEngineFactory ocrEngineFactory,
        ITranslatorEngineFactory translatorEngineFactory,
        ISettings settings)
    {
        _ocrEngineFactory = ocrEngineFactory;
        _translatorEngineFactory = translatorEngineFactory;
        _settings = settings;
    }

    /// <summary>
    /// Carga settings, motor de OCR y traductor. Todo o nada: las propiedades solo se asignan si
    /// las tres piezas se crearon bien, porque Coordinator usa AppSettings como bandera de "listo".
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var appSettings = _settings.Load();
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
        AppSettings = appSettings;
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
