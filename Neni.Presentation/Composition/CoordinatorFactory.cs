using Neni.Application.Interfaces;
using Neni.Application.Services;
using Neni.Imaging.Services;
using Neni.Ocr.Services;
using Neni.Platform.Services.Linux;
using Neni.Presentation.Services;
using Neni.Translation.Services;

namespace Neni.Presentation.Composition;

// Composition root de la app: el único lugar de Neni.Presentation autorizado a conocer
// implementaciones concretas de las demás capas. ViewModels/Views solo ven ICoordinator.
//
// Cableado fijo a Linux por ahora (no hay todavía nada que elegir Windows-vs-Linux en runtime);
// revisar esto cuando exista una implementación de Neni.Platform para Windows.
//
// Piezas que todavía no hacen el trabajo real (ver sus propios archivos): IOverlay no dibuja nada,
// y tanto ITranslator como IFrameProcessor.ProcessFrame devuelven su entrada intacta para poder
// ejercitar el ciclo sin traducción ni preprocesado.
// StartCycle/ProcessCycle fallarán o no harán nada útil hasta que se reemplacen; OpenWindowSelectorAsync,
// AttachToTargetAsync y GrabPreviewFrameAsync (los únicos que Select Window usa) no los tocan.
public static class CoordinatorFactory
{
    public static ICoordinator Create()
    {
        var portalSession = new PortalScreenCastSession();
        var targetSelector = new LinuxCaptureTargetSelector(portalSession);
        var frameCapture = new LinuxFrameCapture(portalSession);

        var settings = new DefaultSettings();
        var ocrEngineFactory = new RapidOcrEngineFactory(settings);
        var translatorEngineFactory = new PassthroughTranslatorEngineFactory();
        var initialize = new Initialize(ocrEngineFactory, translatorEngineFactory, settings);

        var overlay = new NoOpOverlay();
        var frameProcessor = new FrameProcessor();
        var deduplication = new Deduplication(settings);
        var regionOfInterest = new RegionOfInterest(settings);

        return new Coordinator(
            initialize,
            deduplication,
            regionOfInterest,
            overlay,
            frameProcessor,
            frameCapture,
            targetSelector);
    }
}
