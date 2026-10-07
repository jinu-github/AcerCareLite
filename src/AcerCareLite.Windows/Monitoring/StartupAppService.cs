using AcerCareLite.Core.Utilities;
using Microsoft.Win32;

namespace AcerCareLite.Windows.Monitoring;

/// <summary>Lists startup entries and flips Task Manager's StartupApproved flag for the current user's entries only.</summary>
public sealed class StartupAppService : IStartupAppService
{
    private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKey32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";

    public IReadOnlyList<StartupEntry> List()
    {
        var list = new List<StartupEntry>();
        Try(() => AddRun(list, Registry.CurrentUser, RunKey, StartupLocation.UserRun));
        Try(() => AddRun(list, Registry.LocalMachine, RunKey, StartupLocation.MachineRun));
        Try(() => AddRun(list, Registry.LocalMachine, RunKey32, StartupLocation.MachineRun32));
        Try(() => AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupLocation.UserFolder));
        Try(() => AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupLocation.MachineFolder));
        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool SetEnabled(StartupEntry entry, bool enabled)
    {
        if (!entry.CanModify) return false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(ApprovedRoot + ApprovedSubKey(entry.Location), writable: true);
            key.SetValue(entry.Name, StartupApproval.Encode(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
            return true;
        }
        catch { return false; }
    }

    private static void AddRun(List<StartupEntry> list, RegistryKey hive, string path, StartupLocation location)
    {
        using var key = hive.OpenSubKey(path);
        if (key == null) return;
        foreach (var name in key.GetValueNames())
        {
            if (name.Length == 0) continue;
            list.Add(new StartupEntry(name, key.GetValue(name)?.ToString() ?? "", location, IsEnabled(location, name)));
        }
    }

    private static void AddFolder(List<StartupEntry> list, string folder, StartupLocation location)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new StartupEntry(name, file, location, IsEnabled(location, name)));
        }
    }

    private static bool IsEnabled(StartupLocation location, string name)
    {
        try
        {
            var hive = location is StartupLocation.UserRun or StartupLocation.UserFolder ? Registry.CurrentUser : Registry.LocalMachine;
            using var key = hive.OpenSubKey(ApprovedRoot + ApprovedSubKey(location));
            return StartupApproval.IsEnabled(key?.GetValue(name) as byte[]);
        }
        catch { return true; }
    }

    private static string ApprovedSubKey(StartupLocation location) => location switch
    {
        StartupLocation.MachineRun32 => "Run32",
        StartupLocation.UserRun or StartupLocation.MachineRun => "Run",
        _ => "StartupFolder"
    };

    private static void Try(Action action)
    {
        try { action(); } catch { /* one unreadable source must not hide the others */ }
    }
}
