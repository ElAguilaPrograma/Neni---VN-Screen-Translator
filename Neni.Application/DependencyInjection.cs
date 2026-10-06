using Microsoft.Extensions.DependencyInjection;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;
using Neni.Application.Interfaces;
using Neni.Application.Pipeline;
using Neni.Application.Services;

namespace Neni.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Registra la orquestacion de la pipeline (Coordinator) y lo que necesita para inicializarse.</summary>
    public static IServiceCollection AddNeniApplication(this IServiceCollection services)
    {
        services.AddSingleton<ISettings, DefaultSettings>();
        // Se carga una sola vez, y cada seccion se registra aparte para que cada servicio reciba solo la suya.
        services.AddSingleton(sp => sp.GetRequiredService<ISettings>().Load());
        services.AddSingleton(sp => sp.GetRequiredService<Settings>().Cycle);
        services.AddSingleton(sp => sp.GetRequiredService<Settings>().Roi);
        services.AddSingleton(sp => sp.GetRequiredService<Settings>().Ocr);
        services.AddSingleton(sp => sp.GetRequiredService<Settings>().Preprocessing);
        services.AddSingleton(sp => sp.GetRequiredService<Settings>().Deduplication);
        services.AddSingleton(sp => sp.GetRequiredService<Settings>().Translation);
        services.AddSingleton(sp => sp.GetRequiredService<Settings>().CompanionWindow);
        services.AddSingleton<IPipelineEngines, PipelineEngines>();
        services.AddSingleton(sp => new TranslationCache(
            sp.GetRequiredService<IPipelineEngines>(), sp.GetRequiredService<TranslationSettings>()));
        services.AddSingleton<OverlayTracker>();
        services.AddSingleton<RoiProcessor>();
        services.AddSingleton<CycleRunner>();
        services.AddSingleton<ICoordinator, Coordinator>();
        return services;
    }
}
