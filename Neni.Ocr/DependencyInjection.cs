using Microsoft.Extensions.DependencyInjection;
using Neni.Abstractions.Interfaces;
using Neni.Ocr.Services;

namespace Neni.Ocr;

public static class OcrServiceCollectionExtensions
{
    /// <summary>Registra el motor de OCR (RapidOcr) detras de IOcrEngineFactory.</summary>
    public static IServiceCollection AddNeniOcr(this IServiceCollection services)
    {
        services.AddSingleton(_ => new RapidOcrModelManagerService());
        services.AddSingleton<IOcrEngineFactory>(sp => new RapidOcrEngineFactory(
            sp.GetRequiredService<ISettings>(),
            sp.GetRequiredService<RapidOcrModelManagerService>()));
        return services;
    }
}
