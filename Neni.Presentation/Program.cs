using Avalonia;
using System;
using System.Linq;

namespace Neni.Presentation;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Diagnóstico temporal para aislar el bug del preview estático, ver DiagCapture.cs.
        if (args.Contains("--diag-capture-holdonly"))
        {
            DiagCapture.RunHoldOnlyAsync().GetAwaiter().GetResult();
            return;
        }

        if (args.Contains("--diag-capture"))
        {
            DiagCapture.RunAsync().GetAwaiter().GetResult();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
