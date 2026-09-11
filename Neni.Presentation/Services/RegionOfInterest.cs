using Avalonia.Controls.ApplicationLifetimes;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;
using Neni.Presentation.ViewModels;
using Neni.Presentation.Views;

namespace Neni.Presentation.Services;

// El parámetro Frame de DrawRoisAsync es el único frame que se muestra: un solo GrabFrameAsync
// tomado antes de abrir la ventana, sin preview en vivo mientras el usuario dibuja.
internal sealed class RegionOfInterest : IRegionOfInterest
{
    private readonly ISettings _settings;
    private readonly Dictionary<int, Abstractions.Entities.RegionOfInterest> _rois = new();

    public RegionOfInterest(ISettings settings)
    {
        _settings = settings;
    }

    public async Task<IEnumerable<Abstractions.Entities.RegionOfInterest>> DrawRoisAsync(
        Frame frame, CancellationToken cancellationToken = default)
    {
        var maxRois = _settings.Load().MaxPendingRois;
        var viewModel = new RoiSelectionViewModel(frame, maxRois, _rois.Values);
        var window = new RoiSelectionWindow { DataContext = viewModel };

        var owner = ResolveOwnerWindow();
        if (owner is not null)
            await window.ShowDialog(owner); // Modal: bloquea MainWindow mientras se dibuja.
        else
            await ShowAndWaitForCloseAsync(window);

        // ConfirmedRois queda null si se cerró con la X sin tocar "Confirmar": se conserva el
        // estado previo en vez de perder lo que ya estaba definido.
        if (viewModel.ConfirmedRois is not { } confirmed)
            return _rois.Values;

        _rois.Clear();
        foreach (var roi in confirmed)
            _rois[roi.RoiId] = roi;

        return _rois.Values;
    }

    public void DeleteRoi(int roiId) => _rois.Remove(roiId);

    public void ClearRois() => _rois.Clear();

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
