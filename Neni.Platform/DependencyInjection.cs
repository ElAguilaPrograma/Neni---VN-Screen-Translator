using Microsoft.Extensions.DependencyInjection;
using Neni.Abstractions.Interfaces;
using Neni.Platform.Services.Linux;

namespace Neni.Platform;

public static class PlatformServiceCollectionExtensions
{
    /// <summary>Registra seleccion de ventana y captura de frames para el SO actual.</summary>
    public static IServiceCollection AddNeniPlatform(this IServiceCollection services)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Neni.Platform solo tiene implementacion para Linux por ahora.");

        // Singleton obligatorio: la sesion del portal muere con su conexion D-Bus, y el node id que
        // entrega el selector solo es valido mientras esta misma instancia viva para la captura.
        services.AddSingleton<PortalScreenCastSession>();
        services.AddSingleton<ICaptureTargetSelector, LinuxCaptureTargetSelector>();
        services.AddSingleton<IFrameCapture, LinuxFrameCapture>();
        services.AddSingleton<IPlatformEnvironment, LinuxPlatformEnvironment>();
        return services;
    }
}
