using Neni.Abstractions.Interfaces;
using Neni.Application.Interfaces;

namespace Neni.Application.Services;

// Aqui se debe de inicializar todo lo que sea necesario para que la aplicacion funcione,
// primero se deben inicializar las settings, luego los servicios que dependen de esas settings,
// como el modelo de OCR, el modelo de traducción, etc. Se debe de hacer una sola vez al inicio de la aplicacion.

public sealed class Initialize : IInitialize
{
    private readonly ISettings _settings;
    private readonly IOcrEngineFactory _ocrEngineFactory;
    private readonly ITranslatorEngineFactory _translatorEngineFactory;

    public ISettings Setting { get; set; } = null!;
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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // TODO: El servicio que implemente las configuraciones debera instanciar esta clase llamar a .Load() y inicializar cada configuración.
        Setting = _settings;
        Engine = await _ocrEngineFactory.CreateAsync(cancellationToken);
        Translator = await _translatorEngineFactory.CreateAsync(cancellationToken);
    }

    public async Task DisposeAsync()
    {
        await Engine.DisposeAsync();
        await Translator.DisposeAsync();
    }
}
