using System.Diagnostics;

namespace Kagami.Utilities;

internal static class ProcessHelper
{
    /// <summary>
    /// Get the process start time as an ISO 8601 string.
    /// Returns null if the process cannot be opened.
    /// </summary>
    public static string? GetProcessStartTime(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime().ToString("O");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get the raw creation time via GetProcessTimes for cross-checking.
    /// Returns null if the process cannot be opened.
    /// </summary>
    public static long? GetProcessCreationTimeRaw(int pid)
    {
        var handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);

        if (handle == IntPtr.Zero)
            return null;

        try
        {
            if (NativeMethods.GetProcessTimes(handle, out long creation, out _, out _, out _))
                return creation;

            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>
    /// Get process name (e.g. "notepad.exe") from a PID.
    /// </summary>
    public static string? GetProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName + ".exe";
        }
        catch
        {
            return null;
        }
    }
}
