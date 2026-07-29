using System.Text.Json;
using Kagami.Protocol;
using Kagami.Utilities;

namespace Kagami.Commands;

public class CapabilitiesCommand
{
    private readonly Backends.CaptureService _capture;

    public CapabilitiesCommand(Backends.CaptureService capture)
    {
        _capture = capture;
    }

    public int Run()
    {
        var writer = new Backends.ResponseWriter("capabilities");

        try
        {
            var data = new CapabilitiesData
            {
                WindowsVersion = Environment.OSVersion.Version.ToString(),
                DpiAwareness = "per-monitor-v2",
                CaptureBackends = _capture.GetBackendAvailability(),
                Uia = new UiaCapabilityInfo { Version = 3 },
                MouseCommands = ["move", "double-click", "scroll", "drag"],
                Elevated = IsElevated(),
                InteractiveSession = Environment.UserInteractive
            };

            return writer.Success(data);
        }
        catch (Exception ex)
        {
            return writer.FatalException(ex);
        }
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
