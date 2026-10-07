using System.Runtime.InteropServices;
using AcerCareLite.Core.Utilities;

namespace AcerCareLite.Windows.Monitoring;

/// <summary>Uses the Windows shell's own Recycle Bin calls. Tries all drives at once, then each drive separately.</summary>
public sealed class RecycleBinService : IRecycleBinService
{
    // The shell header packs this struct to 1 byte (20 bytes). A second, default-packed layout is tried as a fallback.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ShQueryRbInfo
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfoPadded
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref ShQueryRbInfo info);

    [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBin", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinPadded(string? rootPath, ref ShQueryRbInfoPadded info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    private const uint NoConfirmation = 0x1, NoProgressUi = 0x2, NoSound = 0x4;
    private const int EUnexpected = unchecked((int)0x8000FFFF); // returned when the bin is already empty

    public string? LastError { get; private set; }

    public RecycleBinInfo? Query()
    {
        LastError = null;
        try
        {
            var hr = TryQuery(null, out var bytes, out var items);
            if (hr == 0) return new RecycleBinInfo(bytes, items);

            long totalBytes = 0, totalItems = 0;
            var any = false;
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                var h = TryQuery(drive.Name, out var b, out var i);
                if (h == 0) { any = true; totalBytes += b; totalItems += i; }
                else hr = h;
            }
            if (any) return new RecycleBinInfo(totalBytes, totalItems);

            LastError = $"error 0x{hr:X8}";
            return null;
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    public bool Empty()
    {
        var hr = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);
        return hr == 0 || hr == EUnexpected;
    }

    private static int TryQuery(string? root, out long bytes, out long items)
    {
        var packed = new ShQueryRbInfo { cbSize = Marshal.SizeOf<ShQueryRbInfo>() };
        var hr = SHQueryRecycleBin(root, ref packed);
        if (hr == 0) { bytes = packed.i64Size; items = packed.i64NumItems; return 0; }

        var padded = new ShQueryRbInfoPadded { cbSize = Marshal.SizeOf<ShQueryRbInfoPadded>() };
        var hr2 = SHQueryRecycleBinPadded(root, ref padded);
        if (hr2 == 0) { bytes = padded.i64Size; items = padded.i64NumItems; return 0; }

        bytes = items = 0;
        return hr;
    }
}
