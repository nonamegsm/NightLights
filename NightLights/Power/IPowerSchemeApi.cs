using System;

namespace NightLights.Power
{
    public interface IPowerSchemeApi
    {
        bool TryGetActiveScheme(out Guid schemeGuid, out string error);
        bool TrySetActiveScheme(Guid schemeGuid, out string error);
    }
}
