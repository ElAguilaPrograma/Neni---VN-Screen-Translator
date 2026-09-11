using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Neni.Presentation.Composition;
using Neni.Presentation.ViewModels;
using Neni.Presentation.Views;

namespace Neni.Presentation;

// Calificado explícitamente: Neni.Application (referenciado para el composition root de abajo)
// es un namespace hermano de Neni.Presentation bajo Neni, y eso vuelve ambiguo "Application" a secas.
public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var coordinator = CoordinatorFactory.Create();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(coordinator),
            };

            // ShutdownRequested es síncrono: hay que cancelar el cierre, liberar y recién entonces
            // cerrar de verdad, o el proceso puede morir a mitad del DisposeAsync.
            var shuttingDown = false;

            desktop.ShutdownRequested += async (_, e) =>
            {
                if (shuttingDown)
                    return;

                shuttingDown = true;
                e.Cancel = true;

                try
                {
                    await coordinator.DisposeAsync();
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