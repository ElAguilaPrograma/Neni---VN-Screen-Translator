using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Neni.Application;
using Neni.Imaging;
using Neni.Ocr;
using Neni.Platform;
using Neni.Presentation;
using Neni.Translation;

namespace Neni.Desktop;

// Host de escritorio y composition root: el unico proyecto que referencia todas las capas. No
// nombra ninguna clase concreta, cada capa se registra a si misma con su modulo AddNeniXxx.
sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // App es duena del provider y lo libera al cerrar (ver App.OnFrameworkInitializationCompleted).
        var services = BuildServices();

        BuildAvaloniaApp(() => new App(services)).StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Arma el contenedor. ValidateOnBuild hace que un registro faltante falle al arrancar y no al
    /// primer uso.
    /// </summary>
    private static ServiceProvider BuildServices()
        => new ServiceCollection()
            .AddNeniApplication()
            .AddNeniOcr()
            .AddNeniImaging()
            .AddNeniTranslation()
            .AddNeniPlatform()
            .AddNeniPresentation()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

    // Avalonia configuration, don't remove; also used by visual designer (sin servicios).
    public static AppBuilder BuildAvaloniaApp()
        => BuildAvaloniaApp(() => new App());

    private static AppBuilder BuildAvaloniaApp(Func<App> appFactory)
        => AppBuilder.Configure(appFactory)
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
