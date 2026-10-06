using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;
using Neni.Ocr.Services;

namespace Neni.Ocr;

public static class OcrServiceCollectionExtensions
{
    /// <summary>Registra el motor de OCR (RapidOcr) detras de IOcrEngineFactory y la normalizacion de su texto.</summary>
    public static IServiceCollection AddNeniOcr(this IServiceCollection services)
    {
        services.AddSingleton(sp => new RapidOcrModelManagerService(
            logger: sp.GetRequiredService<ILogger<RapidOcrModelManagerService>>()));
        services.AddSingleton<IOcrEngineFactory>(sp => new RapidOcrEngineFactory(
            sp.GetRequiredService<OcrSettings>(),
            sp.GetRequiredService<RapidOcrModelManagerService>(),
            logger: sp.GetRequiredService<ILogger<Services.Ocr>>()));
        services.AddSingleton<ITextNormalizer, ScriptTextNormalizer>();
        return services;
    }
}
