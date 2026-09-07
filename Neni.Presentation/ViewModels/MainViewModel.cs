using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Neni.Application.Interfaces;

namespace Neni.Presentation.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ICoordinator _coordinator;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Ninguna ventana seleccionada.";

    public MainViewModel(ICoordinator coordinator)
        => _coordinator = coordinator;

    [RelayCommand]
    private async Task SelectWindowAsync()
    {
        try
        {
            StatusMessage = "Selecciona una ventana en el diálogo del sistema...";

            var targets = await _coordinator.OpenWindowSelectorAsync();
            var target = targets.FirstOrDefault();

            if (target is null)
            {
                StatusMessage = "Selección cancelada.";
                return;
            }

            await _coordinator.AttachToTargetAsync(target);

            var frame = await _coordinator.GrabPreviewFrameAsync();
            StatusMessage = $"Sesión de captura activa en '{target.Name}' ({frame.Width}x{frame.Height}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al seleccionar/adjuntar la ventana: {ex.Message}";
        }
    }
}
