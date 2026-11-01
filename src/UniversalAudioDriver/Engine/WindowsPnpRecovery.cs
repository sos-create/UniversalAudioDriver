using System.Diagnostics;
using System.Security.Principal;

namespace UniversalAudioDriver.Engine;

public sealed class WindowsPnpRecovery
{
    public int RescanDevices()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("PnP recovery is available only on Windows.");

        var startInfo = new ProcessStartInfo
        {
            FileName = "pnputil.exe",
            Arguments = "/scan-devices",
            UseShellExecute = true,
            Verb = IsAdministrator() ? string.Empty : "runas"
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows could not start pnputil.exe.");

        process.WaitForExit();
        return process.ExitCode;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
