using Microsoft.Extensions.DependencyInjection;
using Neni.Abstractions.Interfaces;
using Neni.Application.Interfaces;
using Neni.Application.Services;

namespace Neni.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Registra la orquestacion de la pipeline (Coordinator) y lo que necesita para inicializarse.</summary>
    public static IServiceCollection AddNeniApplication(this IServiceCollection services)
    {
        services.AddSingleton<ISettings, DefaultSettings>();
        services.AddSingleton<IInitialize, Initialize>();
        services.AddSingleton<ICoordinator, Coordinator>();
        return services;
    }
}
