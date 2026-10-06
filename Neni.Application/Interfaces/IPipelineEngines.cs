using Neni.Abstractions.Interfaces;

namespace Neni.Application.Interfaces;

// Ciclo de vida de los dos motores pesados de la pipeline (OCR y traductor): cuando quedan listos
// y quien los libera. No expone nada mas; las settings llegan a cada servicio por su constructor.
internal interface IPipelineEngines : IAsyncDisposable
{
    // true solo cuando InitializeAsync termino bien y los dos motores existen.
    bool IsReady { get; }

    // Lanzan InvalidOperationException mientras IsReady sea false.
    IOcr Ocr { get; }
    ITranslator Translator { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
}
