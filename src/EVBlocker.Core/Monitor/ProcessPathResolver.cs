using System.Runtime.InteropServices;

namespace EVBlocker.Core.Monitor;

/// <summary>
/// Resolves a process id to the full path of its executable.
///
/// Deliberately stateless: no cross-scan cache. A PID cache would have to guard against PID
/// reuse (a recycled PID would attribute a connection to the wrong executable), and the call
/// itself is cheap enough that caching is not worth that bug. The scanner dedupes per scan
/// instead, so a browser with 50 sockets costs one lookup, not fifty.
/// </summary>
internal static partial class ProcessPathResolver
{
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    /// <summary>
    /// Full executable path, or null when the process cannot be opened. That happens for
    /// PID 0 / PID 4 (kernel), for protected processes such as antivirus, and for a process
    /// that exited between reading the connection table and this lookup.
    /// </summary>
    public static unsafe string? TryGetPath(int processId)
    {
        // PID 0 is System Idle and PID 4 is System; neither has a user-mode image to report.
        if (processId <= 4)
        {
            return null;
        }

        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            // Long paths can exceed MAX_PATH, so start well above it rather than retry-looping.
            const int capacity = 1024;
            char* buffer = stackalloc char[capacity];
            int size = capacity;

            if (QueryFullProcessImageName(handle, 0, buffer, ref size) == 0)
            {
                return null;
            }

            return new string(buffer, 0, size);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(int dwDesiredAccess, int bInheritHandle, int dwProcessId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    private static unsafe partial int QueryFullProcessImageName(
        IntPtr hProcess, int dwFlags, char* lpExeName, ref int lpdwSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int CloseHandle(IntPtr hObject);
}
