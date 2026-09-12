using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Neni.Abstractions.Entities;
using Neni.Application.Interfaces;

namespace Neni.Presentation.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ICoordinator _coordinator;
    private IReadOnlyList<RegionOfInterest> _rois = [];

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Ninguna ventana seleccionada.";

    [ObservableProperty]
    public partial string DetectedText { get; set; } = "";

    // Latido del ciclo: si sube, la pipeline sigue viva aunque el texto no cambie.
    [ObservableProperty]
    public partial int CycleReports { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InitializeCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCycleCommand))]
    public partial bool IsInitialized { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCycleCommand))]
    [NotifyCanExecuteChangedFor(nameof(DrawRoisCommand))]
    public partial bool HasTarget { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCycleCommand))]
    public partial bool HasRois { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCycleCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCycleCommand))]
    [NotifyCanExecuteChangedFor(nameof(DrawRoisCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectWindowCommand))]
    public partial bool IsCycleRunning { get; set; }

    public MainViewModel(ICoordinator coordinator)
        => _coordinator = coordinator;

    private bool CanInitialize => !IsInitialized;

    private bool CanSelectWindow => !IsCycleRunning;

    private bool CanDrawRois => HasTarget && !IsCycleRunning;

    private bool CanStartCycle => IsInitialized && HasTarget && HasRois && !IsCycleRunning;

    private bool CanStopCycle => IsCycleRunning;

    /// <summary>Carga los modelos del pipeline y habilita el resto de la UI.</summary>
    [RelayCommand(CanExecute = nameof(CanInitialize))]
    private async Task InitializeAsync()
    {
        try
        {
            StatusMessage = "Cargando modelos... la primera vez hay que descargarlos, puede tardar.";
            await _coordinator.InitializeAsync();
            IsInitialized = true;
            StatusMessage = "Modelos cargados.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar los modelos: {ex.Message}";
        }
    }

    /// <summary>Abre el selector del sistema y vincula la ventana elegida a la sesion de captura.</summary>
    [RelayCommand(CanExecute = nameof(CanSelectWindow))]
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
            HasTarget = true;
            StatusMessage = $"Sesión de captura activa en '{target.Name}' ({frame.Width}x{frame.Height}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al seleccionar/adjuntar la ventana: {ex.Message}";
        }
    }

    /// <summary>Abre la ventana de dibujo y se queda con las ROIs que el usuario confirme.</summary>
    [RelayCommand(CanExecute = nameof(CanDrawRois))]
    private async Task DrawRoisAsync()
    {
        try
        {
            var frame = await _coordinator.GrabPreviewFrameAsync();
            _rois = (await _coordinator.GetRegionOfInterestAsync(frame)).ToList();
            HasRois = _rois.Count > 0;
            StatusMessage = $"{_rois.Count} ROI(s) definidas.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al dibujar ROIs: {ex.Message}";
        }
    }

    /// <summary>Arranca el ciclo en segundo plano; no lo espera, porque no retorna hasta pararse.</summary>
    [RelayCommand(CanExecute = nameof(CanStartCycle))]
    private void StartCycle()
    {
        IsCycleRunning = true;
        CycleReports = 0;
        DetectedText = "";
        StatusMessage = "Ciclo en marcha.";

        // Se crea aqui, en el hilo de UI, para que Progress<T> capture su contexto y los reportes
        // vuelvan solos a ese hilo.
        var progress = new Progress<IReadOnlyDictionary<int, string>>(OnCycleReported);

        _ = RunCycleAsync(progress);
    }

    /// <summary>Detiene el ciclo y espera a que la pipeline termine de limpiar.</summary>
    [RelayCommand(CanExecute = nameof(CanStopCycle))]
    private async Task StopCycleAsync()
    {
        try
        {
            await _coordinator.StopCycle();
            StatusMessage = $"Ciclo detenido tras {CycleReports} vueltas.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al detener el ciclo: {ex.Message}";
        }
        finally
        {
            IsCycleRunning = false;
        }
    }

    /// <summary>Envuelve el ciclo para sacarlo del hilo de UI y no perder sus excepciones.</summary>
    private async Task RunCycleAsync(IProgress<IReadOnlyDictionary<int, string>> progress)
    {
        try
        {
            // Task.Run es lo que mantiene el ciclo fuera del hilo de UI: sin el, cada await del
            // bucle volveria a ese hilo y el recorte, la deduplicacion y la espera lo bloquearian.
            await Task.Run(() => _coordinator.StartCycle(_rois, progress));
        }
        catch (Exception ex)
        {
            StatusMessage = $"El ciclo se detuvo por un error: {ex.Message}";
        }
        finally
        {
            IsCycleRunning = false;
        }
    }

    private void OnCycleReported(IReadOnlyDictionary<int, string> textByRoi)
    {
        CycleReports++;
        DetectedText = string.Join(
            Environment.NewLine,
            textByRoi.OrderBy(entry => entry.Key).Select(entry => $"[ROI {entry.Key}] {entry.Value}"));
    }
}
