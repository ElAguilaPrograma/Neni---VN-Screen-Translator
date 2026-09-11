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
// IOverlay, IDeduplication e ITranslator todavía son placeholders temporales (ver sus propios
// archivos) — no implementan nada real, solo permiten construir el Coordinator. De IFrameProcessor
// solo CropFrame es real; ProcessFrame sigue pendiente y lanza NotImplementedException.
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
        var translatorEngineFactory = new NotImplementedTranslatorEngineFactory();
        var initialize = new Initialize(ocrEngineFactory, translatorEngineFactory, settings);

        var overlay = new NoOpOverlay();
        var frameProcessor = new FrameProcessor();
        var deduplication = new NotImplementedDeduplication();
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
