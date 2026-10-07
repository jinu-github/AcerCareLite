using AcerCareLite.Core.Monitoring;
using AcerCareLite.Core.Presentation;
using AcerCareLite.Core.Utilities;
using Xunit;

namespace AcerCareLite.Tests;

public class UtilitiesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "acl-cleaner-" + Guid.NewGuid().ToString("N"));
    private readonly string _sibling;

    public UtilitiesTests()
    {
        Directory.CreateDirectory(_root);
        _sibling = _root + "-sibling";
        Directory.CreateDirectory(_sibling);
    }

    public void Dispose()
    {
        foreach (var d in new[] { _root, _sibling })
            try { Directory.Delete(d, true); } catch { }
    }

    private string File(string relative, int daysOld, string content = "data")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-daysOld));
        return path;
    }

    // ---- cleaner ----
    [Fact]
    public void Cleaner_deletes_only_old_files_and_removes_emptied_folders()
    {
        var oldFile = File("old.txt", 3);
        var newFile = File("new.txt", 0);
        var nestedOld = File(Path.Combine("sub", "deep", "old.log"), 5);

        var cleaner = new TempFileCleaner(_root);
        Assert.Equal(2, cleaner.Scan().Files);

        var result = cleaner.Clean();

        Assert.Equal(2, result.DeletedFiles);
        Assert.False(System.IO.File.Exists(oldFile));
        Assert.False(System.IO.File.Exists(nestedOld));
        Assert.True(System.IO.File.Exists(newFile));
        Assert.False(Directory.Exists(Path.Combine(_root, "sub")));
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void Cleaner_never_touches_sibling_folders()
    {
        var outside = Path.Combine(_sibling, "old.txt");
        System.IO.File.WriteAllText(outside, "keep");
        System.IO.File.SetLastWriteTimeUtc(outside, DateTime.UtcNow.AddDays(-10));
        File("old.txt", 10);

        new TempFileCleaner(_root).Clean();

        Assert.True(System.IO.File.Exists(outside));
    }

    [Fact]
    public void Cleaner_skips_files_in_use()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = File("locked.txt", 4);
        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = new TempFileCleaner(_root).Clean();

        Assert.Equal(0, result.DeletedFiles);
        Assert.Equal(1, result.Skipped);
        Assert.True(System.IO.File.Exists(path));
    }

    [Fact]
    public void Cleaner_refuses_a_drive_root()
    {
        var driveRoot = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Throws<ArgumentException>(() => new TempFileCleaner(driveRoot));
    }

    // ---- startup approval flag ----
    [Fact]
    public void Startup_flag_follows_task_manager_convention()
    {
        Assert.True(StartupApproval.IsEnabled(null));
        Assert.True(StartupApproval.IsEnabled(new byte[] { 2, 0, 0, 0 }));
        Assert.True(StartupApproval.IsEnabled(new byte[] { 6, 0, 0, 0 }));
        Assert.False(StartupApproval.IsEnabled(new byte[] { 3, 0, 0, 0 }));
        Assert.False(StartupApproval.IsEnabled(new byte[] { 7, 0, 0, 0 }));

        var disabled = StartupApproval.Encode(false, DateTime.UtcNow);
        Assert.Equal(12, disabled.Length);
        Assert.False(StartupApproval.IsEnabled(disabled));
        Assert.True(StartupApproval.IsEnabled(StartupApproval.Encode(true, DateTime.UtcNow)));
    }

    // ---- view model ----
    private sealed class FakeStartup : IStartupAppService
    {
        public List<(StartupEntry Entry, bool Enabled)> Calls = new();
        public IReadOnlyList<StartupEntry> List() => new[]
        {
            new StartupEntry("MyApp", "c:\\app.exe", StartupLocation.UserRun, true),
            new StartupEntry("Driver", "c:\\drv.exe", StartupLocation.MachineRun, true)
        };
        public bool SetEnabled(StartupEntry entry, bool enabled) { Calls.Add((entry, enabled)); return true; }
    }

    private sealed class FakeTemp : ITempCleaner
    {
        public int CleanCalls;
        public TempScanResult Scan() => new(3, 3L << 20);
        public TempCleanResult Clean() { CleanCalls++; return new(3, 3L << 20, 0); }
    }

    private sealed class FakeRecycle : IRecycleBinService
    {
        public int EmptyCalls;
        public RecycleBinInfo? Query() => new(5L << 20, 7);
        public bool Empty() { EmptyCalls++; return true; }
    }

    private sealed class FakeStorage : IStorageMonitor
    {
        public IReadOnlyList<DriveUsage> Read() => new[] { new DriveUsage("C:", 100UL << 30, 40UL << 30) };
    }

    private sealed class FakeConfirm : IConfirmationService
    {
        public bool Answer;
        public int Asked;
        public bool Confirm(string title, string message) { Asked++; return Answer; }
    }

    private static (UtilitiesViewModel Vm, FakeStartup Startup, FakeTemp Temp, FakeRecycle Recycle, FakeConfirm Confirm) Create(bool confirm)
    {
        var s = new FakeStartup(); var t = new FakeTemp(); var r = new FakeRecycle(); var c = new FakeConfirm { Answer = confirm };
        return (new UtilitiesViewModel(s, t, r, new FakeStorage(), c), s, t, r, c);
    }

    [Fact]
    public async Task Declined_confirmation_deletes_nothing()
    {
        var (vm, _, temp, recycle, confirm) = Create(false);
        await vm.LoadAsync();
        await vm.CleanTempCommand.ExecuteAsync(null);
        await vm.EmptyRecycleCommand.ExecuteAsync(null);

        Assert.Equal(2, confirm.Asked);
        Assert.Equal(0, temp.CleanCalls);
        Assert.Equal(0, recycle.EmptyCalls);
    }

    [Fact]
    public async Task Confirmed_actions_run_and_report()
    {
        var (vm, _, temp, recycle, _) = Create(true);
        await vm.LoadAsync();

        await vm.CleanTempCommand.ExecuteAsync(null);
        Assert.Equal(1, temp.CleanCalls);
        Assert.Contains("deleted 3 files", vm.ResultText);

        await vm.EmptyRecycleCommand.ExecuteAsync(null);
        Assert.Equal(1, recycle.EmptyCalls);
        Assert.Contains("emptied", vm.ResultText);
    }

    [Fact]
    public async Task Startup_toggle_flips_user_entries_and_refuses_machine_entries()
    {
        var (vm, startup, _, _, _) = Create(true);
        await vm.LoadAsync();
        var user = vm.StartupRows.Single(r => r.Name == "MyApp");
        var machine = vm.StartupRows.Single(r => r.Name == "Driver");

        await vm.ToggleStartupCommand.ExecuteAsync(user);
        await vm.ToggleStartupCommand.ExecuteAsync(machine);

        var call = Assert.Single(startup.Calls);
        Assert.Equal("MyApp", call.Entry.Name);
        Assert.False(call.Enabled);
        Assert.False(machine.CanToggle);
    }
}
