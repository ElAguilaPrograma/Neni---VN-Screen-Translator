using Neni.Abstractions.Entities;
using Neni.Application.DataTransferObjets;

namespace Neni.Application.Interfaces;

public interface ICoordinator
{
	IEnumerable<WindowInfo> GetTargetWindow();
	WindowInfo GetTargetWindowInfo(IntPtr handle);
	Task<RegionOfInterest> GetRegionOfInterestAsync(Frame frame, CancellationToken cancellationToken = default);
	void DeleteRegionOfInterest(int roiId);
	Task StartCycle(IntPtr targetWindowHandle, IEnumerable<RegionOfInterestDto>? activeRoisDto = null);
	Task StopCycle();
	Task<string?> ProcessCycle(IEnumerable<RegionOfInterestDto>? activeRoisDto = null);
}