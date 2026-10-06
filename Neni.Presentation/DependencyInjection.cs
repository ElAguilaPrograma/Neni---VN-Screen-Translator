using Microsoft.Extensions.DependencyInjection;
using Neni.Abstractions.Interfaces;
using Neni.Presentation.Services;
using Neni.Presentation.ViewModels;

namespace Neni.Presentation;

public static class PresentationServiceCollectionExtensions
{
    /// <summary>Registra los puertos que implementa la UI (overlay, dibujo de ROIs) y los ViewModels raiz.</summary>
    public static IServiceCollection AddNeniPresentation(this IServiceCollection services)
    {
        services.AddSingleton<IOverlay, NoOpOverlay>();
        services.AddSingleton<IRegionOfInterest, RegionOfInterest>();
        services.AddSingleton<MainViewModel>();
        return services;
    }
}
