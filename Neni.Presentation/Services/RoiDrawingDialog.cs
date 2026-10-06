using Avalonia.Controls.ApplicationLifetimes;
using Neni.Abstractions.Entities;
using Neni.Presentation.ViewModels;
using Neni.Presentation.Views;

namespace Neni.Presentation.Services;

// El frame es el único que se muestra: un solo GrabFrameAsync tomado antes de abrir la ventana,
// sin preview en vivo mientras el usuario dibuja. No guarda ROIs: el dueño es el ICoordinator.
internal sealed class RoiDrawingDialog : IRoiDrawingDialog
{
    private readonly RoiSettings _settings;

    public RoiDrawingDialog(RoiSettings settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlyList<RegionOfInterest>?> ShowAsync(
        Frame frame,
        IReadOnlyList<RegionOfInterest> currentRois,
        CancellationToken cancellationToken = default)
    {
        var viewModel = new RoiSelectionViewModel(frame, _settings.MaxPendingRois, currentRois);
        var window = new RoiSelectionWindow { DataContext = viewModel };

        var owner = ResolveOwnerWindow();
        if (owner is not null)
            await window.ShowDialog(owner); // Modal: bloquea MainWindow mientras se dibuja.
        else
            await ShowAndWaitForCloseAsync(window);

        // null si se cerró con la X sin tocar "Confirmar": quien llama conserva lo que ya tenía.
        return viewModel.ConfirmedRois;
    }

    // Sin owner (p. ej. arrancando fuera del ciclo de vida clásico de escritorio) igual mostramos
    // la ventana, solo que no queda anclada ni bloquea a MainWindow.
    private static Task ShowAndWaitForCloseAsync(RoiSelectionWindow window)
    {
        var tcs = new TaskCompletionSource();
        window.Closed += (_, _) => tcs.TrySetResult();
        window.Show();
        return tcs.Task;
    }

    // Calificado explícitamente: Neni.Application es un namespace hermano bajo Neni y vuelve
    // ambiguo "Application" a secas (mismo problema que documenta App.axaml.cs).
    private static Avalonia.Controls.Window? ResolveOwnerWindow()
        => (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
