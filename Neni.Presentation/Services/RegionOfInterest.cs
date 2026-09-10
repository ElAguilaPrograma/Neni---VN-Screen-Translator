using Avalonia.Controls.ApplicationLifetimes;
using Neni.Abstractions.Entities;
using Neni.Abstractions.Interfaces;
using Neni.Presentation.ViewModels;
using Neni.Presentation.Views;

namespace Neni.Presentation.Services;

// Implementación real de IRegionOfInterest (ver CLAUDE.md, "Implementation ownership decisions":
// dibujar ventanas/rectángulos en pantalla es cosa de Avalonia, no de Neni.Imaging).
//
// Recibe el MISMO IFrameCapture singleton que usa Coordinator (cableado en CoordinatorFactory) y
// lo usa para pedir frames en vivo mientras la ventana de dibujo está abierta. El parámetro Frame
// de DrawRoisAsync solo sirve para pintar el primer cuadro antes de que arranque ese loop.
internal sealed class RegionOfInterest : IRegionOfInterest
{
    private readonly IFrameCapture _frameCapture;
    private readonly ISettings _settings;
    private readonly Dictionary<int, Abstractions.Entities.RegionOfInterest> _rois = new();

    public RegionOfInterest(IFrameCapture frameCapture, ISettings settings)
    {
        _frameCapture = frameCapture;
        _settings = settings;
    }

    public async Task<IEnumerable<Abstractions.Entities.RegionOfInterest>> DrawRoisAsync(
        Frame frame, CancellationToken cancellationToken = default)
    {
        var maxRois = _settings.Load().MaxPendingRois;
        var viewModel = new RoiSelectionViewModel(frame, _frameCapture, maxRois, _rois.Values);
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
