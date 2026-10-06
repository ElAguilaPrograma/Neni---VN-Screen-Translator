using Microsoft.Extensions.DependencyInjection;
using Neni.Abstractions.Interfaces;
using Neni.Imaging.Services;

namespace Neni.Imaging;

public static class ImagingServiceCollectionExtensions
{
    /// <summary>Registra el recorte/preprocesado de frames y la deduplicacion.</summary>
    public static IServiceCollection AddNeniImaging(this IServiceCollection services)
    {
        services.AddSingleton<IFrameProcessor, FrameProcessor>();
        services.AddSingleton<IDeduplication, Deduplication>();
        return services;
    }
}
