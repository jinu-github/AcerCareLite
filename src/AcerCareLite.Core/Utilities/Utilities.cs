namespace AcerCareLite.Core.Utilities;

public enum StartupLocation { UserRun, MachineRun, MachineRun32, UserFolder, MachineFolder }

public sealed record StartupEntry(string Name, string Command, StartupLocation Location, bool Enabled)
{
    /// <summary>Only the current user's entries can be changed without administrator rights.</summary>
    public bool CanModify => Location is StartupLocation.UserRun or StartupLocation.UserFolder;

    public string SourceText => Location switch
    {
        StartupLocation.UserRun => "Registry (current user)",
        StartupLocation.MachineRun => "Registry (all users)",
        StartupLocation.MachineRun32 => "Registry (all users, 32-bit)",
        StartupLocation.UserFolder => "Startup folder (current user)",
        _ => "Startup folder (all users)"
    };
}

public interface IStartupAppService
{
    IReadOnlyList<StartupEntry> List();
    /// <summary>Flips the enabled flag the same way Task Manager does. Never deletes anything.</summary>
    bool SetEnabled(StartupEntry entry, bool enabled);
}

/// <summary>Task Manager's StartupApproved value: first byte even = enabled, odd = disabled, then a timestamp.</summary>
public static class StartupApproval
{
    public static bool IsEnabled(byte[]? data) => data == null || data.Length == 0 || (data[0] & 1) == 0;

    public static byte[] Encode(bool enabled, DateTime utcNow)
    {
        var bytes = new byte[12];
        if (enabled) { bytes[0] = 2; return bytes; }
        bytes[0] = 3;
        BitConverter.GetBytes(utcNow.ToFileTimeUtc()).CopyTo(bytes, 4);
        return bytes;
    }
}

public sealed record RecycleBinInfo(long Bytes, long Items);

public interface IRecycleBinService
{
    RecycleBinInfo? Query();
    bool Empty();
    /// <summary>Why the last Query failed, if it did. Shown next to "Not available".</summary>
    string? LastError => null;
}

public sealed record TempScanResult(long Files, long Bytes);
public sealed record TempCleanResult(long DeletedFiles, long DeletedBytes, long Skipped);

public interface ITempCleaner
{
    TempScanResult Scan();
    TempCleanResult Clean();
}

/// <summary>Used when the temp folder looks unsafe to clean (for example a redirected drive root).</summary>
public sealed class DisabledTempCleaner : ITempCleaner
{
    public TempScanResult Scan() => new(0, 0);
    public TempCleanResult Clean() => new(0, 0, 0);
}

public interface IConfirmationService { bool Confirm(string title, string message); }
