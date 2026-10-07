using System.Runtime.InteropServices;
using System.Text;
using AcerCareLite.Core.Acer;
using Microsoft.Win32.SafeHandles;

namespace AcerCareLite.Acer;

/// <summary>
/// The ONLY file in the solution's Acer code that uses native imports (also linked into the elevated helper).
/// It answers one question: which program is on the other end of this pipe? It opens that process with
/// query-only rights and never touches its memory or its token. HelperGuardTests pins the exact import list.
/// </summary>
internal static class PeerProcess
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public static bool TryGetClientProcessId(SafePipeHandle pipe, out uint pid) => GetNamedPipeClientProcessId(pipe, out pid);

    public static bool TryGetServerProcessId(SafePipeHandle pipe, out uint pid) => GetNamedPipeServerProcessId(pipe, out pid);

    /// <summary>Win32 image path of a process, or null if it cannot be determined (which callers treat as untrusted).</summary>
    public static string? TryGetImagePath(uint pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(32768);
            var size = (uint)buffer.Capacity;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString(0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}

/// <summary>Checks the process at the other end of the pipe against the one file we expect.</summary>
internal static class PeerVerifier
{
    // Development builds only: the helper may also accept the .NET host as the app process (for `dotnet run`).
    // Release builds compile this to false: the peer must be exactly the expected executable.
#if DEBUG
    private const bool AllowDevelopmentHost = true;
#else
    private const bool AllowDevelopmentHost = false;
#endif

    /// <summary>App side: the process that connected must be exactly the helper file we launched. No development exception.</summary>
    public static bool VerifyClient(SafePipeHandle pipe, string expectedImagePath, out string detail) =>
        Verify(PeerProcess.TryGetClientProcessId(pipe, out var pid), pid, expectedImagePath, false, "client", out detail);

    /// <summary>Helper side, change operations: no development exception under any build configuration.</summary>
    public static bool VerifyServerStrict(SafePipeHandle pipe, string expectedImagePath, out string detail) =>
        Verify(PeerProcess.TryGetServerProcessId(pipe, out var pid), pid, expectedImagePath, false, "server", out detail);

    /// <summary>Helper side: the process that created the pipe must be the main application next to us.</summary>
    public static bool VerifyServer(SafePipeHandle pipe, string expectedImagePath, out string detail) =>
        Verify(PeerProcess.TryGetServerProcessId(pipe, out var pid), pid, expectedImagePath, AllowDevelopmentHost, "server", out detail);

    private static bool Verify(bool gotPid, uint pid, string expectedImagePath, bool allowDevelopmentHost, string role, out string detail)
    {
        if (!gotPid)
        {
            detail = $"Could not identify the {role} process.";
            return false;
        }

        var image = PeerProcess.TryGetImagePath(pid);
        if (PeerPolicy.IsTrustedPeer(image, expectedImagePath, allowDevelopmentHost))
        {
            detail = image ?? "";
            return true;
        }

        detail = $"Unexpected {role} process (pid {pid}): {image ?? "image unknown"}";
        return false;
    }
}
