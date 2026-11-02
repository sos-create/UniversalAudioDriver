using System.Runtime.InteropServices;
using System.Text;

namespace UniversalAudioDriver.Discovery;

/// <summary>
/// Reads the Plug and Play parent chain through Configuration Manager.
/// This is discovery only: it does not modify device configuration or drivers.
/// </summary>
internal static class PnpDeviceTopologyDiscovery
{
    private const int CrSuccess = 0;
    private const int MaxParentDepth = 12;

    public static IReadOnlyList<string> GetParentDeviceIds(string deviceId)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(deviceId))
            return Array.Empty<string>();

        try
        {
            if (CM_Locate_DevNodeW(out var currentNode, deviceId, 0) != CrSuccess)
                return Array.Empty<string>();

            var parents = new List<string>();
            var visited = new HashSet<uint> { currentNode };

            for (var depth = 0; depth < MaxParentDepth; depth++)
            {
                if (CM_Get_Parent(out var parentNode, currentNode, 0) != CrSuccess)
                    break;

                if (!visited.Add(parentNode))
                    break;

                if (CM_Get_Device_ID_Size(out var length, parentNode, 0) != CrSuccess)
                    break;

                // CM_Get_Device_ID_Size reports the number of characters excluding NUL.
                var buffer = new StringBuilder(checked((int)length + 1));
                if (CM_Get_Device_IDW(parentNode, buffer, (uint)buffer.Capacity, 0) != CrSuccess)
                    break;

                var parentId = buffer.ToString();
                if (string.IsNullOrWhiteSpace(parentId))
                    break;

                parents.Add(parentId);
                currentNode = parentNode;
            }

            return parents;
        }
        catch (DllNotFoundException)
        {
            return Array.Empty<string>();
        }
        catch (EntryPointNotFoundException)
        {
            return Array.Empty<string>();
        }
        catch (OverflowException)
        {
            return Array.Empty<string>();
        }
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Locate_DevNodeW(
        out uint deviceInstance,
        string deviceId,
        uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern int CM_Get_Parent(
        out uint parentDeviceInstance,
        uint deviceInstance,
        uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern int CM_Get_Device_ID_Size(
        out uint length,
        uint deviceInstance,
        uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_IDW(
        uint deviceInstance,
        StringBuilder buffer,
        uint bufferLength,
        uint flags);
}
