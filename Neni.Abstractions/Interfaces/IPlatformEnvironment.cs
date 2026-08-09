using Neni.Abstractions.Enums;

namespace Neni.Abstractions.Interfaces;

public interface IPlatformEnvironment
{
    DisplayServerType GetDisplayServerType();
    OverlayCapability GetOverlayCapability();
    bool SupportsClickThrough();
}