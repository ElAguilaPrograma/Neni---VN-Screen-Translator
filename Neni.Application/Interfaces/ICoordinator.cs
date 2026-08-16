using Neni.Abstractions.Entities;
using Neni.Application.DataTransferObjets;

namespace Neni.Application.Interfaces;

public interface ICoordinator
{
	Task<IEnumerable<CaptureTarget>> OpenWindowSelectorAsync(bool reuseLastSelection = false, CancellationToken cancellationToken = default);
	Task AttachToTargetAsync(CaptureTarget target, CancellationToken cancellationToken = default);
	Task<IEnumerable<RegionOfInterest>> GetRegionOfInterestAsync(Frame frame, CancellationToken cancellationToken = default);
	void DeleteRegionOfInterest(int roiId);
	Task StartCycle(IEnumerable<RegionOfInterestDto>? activeRoisDto = null);
	Task StopCycle();
	Task<Dictionary<int, string>?> ProcessCycle(IEnumerable<RegionOfInterestDto>? activeRoisDto = null);
}
