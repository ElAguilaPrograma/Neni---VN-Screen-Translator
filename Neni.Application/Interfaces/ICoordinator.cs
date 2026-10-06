using Neni.Abstractions.Entities;

namespace Neni.Application.Interfaces;

// Hereda IAsyncDisposable para detener el ciclo en el cierre antes de que el contenedor libere lo
// que la pipeline sostiene vivo (sesion de ONNX/CUDA, tuberia de GStreamer, sesion D-Bus del portal).
public interface ICoordinator : IAsyncDisposable
{
	/// <summary>
	/// Carga por unica vez lo pesado (motor de OCR, traductor). Hay que esperarlo antes
	/// de StartCycle; la primera vez descarga los modelos, asi que puede tardar.
	/// </summary>
	Task InitializeAsync(CancellationToken cancellationToken = default);
	Task<IEnumerable<CaptureTarget>> OpenWindowSelectorAsync(bool reuseLastSelection = false, CancellationToken cancellationToken = default);
	Task AttachToTargetAsync(CaptureTarget target, CancellationToken cancellationToken = default);
	Task<Frame> GrabPreviewFrameAsync(CancellationToken cancellationToken = default);

	/// <summary>ROIs actuales. Application es su unica duena: la UI las dibuja y se las entrega aqui.</summary>
	IReadOnlyList<RegionOfInterest> RegionsOfInterest { get; }
	/// <summary>
	/// Reemplaza las ROIs (los RoiId deben ser unicos). Valido con el ciclo corriendo: la siguiente
	/// vuelta usa la lista nueva.
	/// </summary>
	void SetRegionsOfInterest(IEnumerable<RegionOfInterest> rois);
	/// <summary>Quita una ROI junto con su texto y su overlay.</summary>
	void DeleteRegionOfInterest(int roiId);

	/// <summary>
	/// Arranca la pipeline sobre las ROIs actuales y la mantiene corriendo hasta StopCycle. No retorna
	/// mientras el ciclo siga vivo. En cada vuelta reporta por progress el texto actual de cada ROI,
	/// indexado por RoiId.
	/// </summary>
	Task StartCycle(IProgress<IReadOnlyDictionary<int, string>>? progress = null);

	/// <summary>Detiene la pipeline, espera a que el ciclo termine y limpia el estado de la sesion.</summary>
	Task StopCycle();

	/// <summary>
	/// Ejecuta una sola vuelta de la pipeline sobre las ROIs actuales. Devuelve el texto de cada ROI
	/// indexado por RoiId (vacio si no hay ROIs), o null si la pipeline no esta activa.
	/// </summary>
	Task<IReadOnlyDictionary<int, string>?> ProcessCycle();
}
