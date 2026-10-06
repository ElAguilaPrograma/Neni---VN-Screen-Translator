using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Neni.Presentation.ViewModels;
using Neni.Presentation.Views;

namespace Neni.Presentation;

// Calificado explícitamente: Neni.Application es un namespace hermano de Neni.Presentation bajo
// Neni, y eso vuelve ambiguo "Application" a secas.
public partial class App : Avalonia.Application
{
    private readonly IServiceProvider? _services;

    /// <summary>Solo para el previewer de XAML: sin servicios no se arma ninguna ventana.</summary>
    public App()
    {
    }

    /// <summary>
    /// Recibe el contenedor ya armado por el host. App pasa a ser su duena: lo libera al cerrar.
    /// </summary>
    public App(IServiceProvider services)
        => _services = services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (_services is not null
            && !Design.IsDesignMode
            && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _services.GetRequiredService<MainViewModel>(),
            };

            // ShutdownRequested es síncrono: hay que cancelar el cierre, liberar y recién entonces
            // cerrar de verdad, o el proceso puede morir a mitad del DisposeAsync. No se libera
            // después de que Main retorna porque para entonces el dispatcher de Avalonia ya no existe.
            var shuttingDown = false;

            desktop.ShutdownRequested += async (_, e) =>
            {
                if (shuttingDown)
                    return;

                shuttingDown = true;
                e.Cancel = true;

                try
                {
                    // El contenedor libera en orden inverso de creación: el Coordinator (creado al
                    // final) detiene el ciclo primero, y la captura cae antes que la sesión del portal
                    // de la que depende.
                    if (_services is IAsyncDisposable disposable)
                        await disposable.DisposeAsync();
                }
                finally
                {
                    // En finally para que un fallo liberando no deje la app imposible de cerrar.
                    desktop.Shutdown();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
