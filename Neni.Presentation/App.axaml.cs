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
        }

        base.OnFrameworkInitializationCompleted();
    }
}