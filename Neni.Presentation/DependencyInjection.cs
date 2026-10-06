using Microsoft.Extensions.DependencyInjection;
using Neni.Abstractions.Interfaces;
using Neni.Presentation.Services;
using Neni.Presentation.ViewModels;

namespace Neni.Presentation;

public static class PresentationServiceCollectionExtensions
{
    /// <summary>Registra el puerto que implementa la UI (overlay), sus servicios internos y los ViewModels raiz.</summary>
    public static IServiceCollection AddNeniPresentation(this IServiceCollection services)
    {
        services.AddSingleton<IOverlay, NoOpOverlay>();
        services.AddSingleton<IRoiDrawingDialog, RoiDrawingDialog>();
        services.AddSingleton<MainViewModel>();
        return services;
    }
}
