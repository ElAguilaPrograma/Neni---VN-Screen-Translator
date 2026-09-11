using Neni.Abstractions.Entities;
using Neni.Application.DataTransferObjets;

namespace Neni.Application.Interfaces;

// Hereda IAsyncDisposable para que el host (App) pueda soltar en el cierre todo lo que la pipeline
// sostiene vivo: sesion de ONNX/CUDA, tuberia de GStreamer y la sesion D-Bus del portal.
public interface ICoordinator : IAsyncDisposable
{
	/// <summary>
	/// Carga por unica vez lo pesado (settings, motor de OCR, traductor). Hay que esperarlo antes
	/// de StartCycle; la primera vez descarga los modelos, asi que puede tardar.
	/// </summary>
	Task InitializeAsync(CancellationToken cancellationToken = default);
	Task<IEnumerable<CaptureTarget>> OpenWindowSelectorAsync(bool reuseLastSelection = false, CancellationToken cancellationToken = default);
	Task AttachToTargetAsync(CaptureTarget target, CancellationToken cancellationToken = default);
	Task<Frame> GrabPreviewFrameAsync(CancellationToken cancellationToken = default);
	Task<IEnumerable<RegionOfInterest>> GetRegionOfInterestAsync(Frame frame, CancellationToken cancellationToken = default);
	void DeleteRegionOfInterest(int roiId);
	/// <summary>
	/// Arranca la pipeline y la mantiene corriendo hasta StopCycle. No retorna mientras el ciclo
	/// siga vivo. En cada vuelta reporta por progress el texto actual de cada ROI, indexado por RoiId.
	/// </summary>
	Task StartCycle(
		IEnumerable<RegionOfInterestDto>? activeRoisDto = null,
		IProgress<IReadOnlyDictionary<int, string>>? progress = null);

	/// <summary>Detiene la pipeline, espera a que el ciclo termine y limpia el estado de la sesion.</summary>
	Task StopCycle();

	/// <summary>
	/// Ejecuta una sola vuelta de la pipeline. Devuelve el texto de cada ROI indexado por RoiId,
	/// o null si la pipeline no esta activa o no hay ROIs.
	/// </summary>
	Task<IReadOnlyDictionary<int, string>?> ProcessCycle(IEnumerable<RegionOfInterestDto>? activeRoisDto = null);
}
