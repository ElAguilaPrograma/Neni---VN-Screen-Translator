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
	Task StartCycle(IEnumerable<RegionOfInterestDto>? activeRoisDto = null);
	Task StopCycle();
	Task<Dictionary<int, string>?> ProcessCycle(IEnumerable<RegionOfInterestDto>? activeRoisDto = null);
}
