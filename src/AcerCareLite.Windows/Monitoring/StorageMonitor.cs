using AcerCareLite.Core.Monitoring;

namespace AcerCareLite.Windows.Monitoring;

public sealed class StorageMonitor : IStorageMonitor
{
    public IReadOnlyList<DriveUsage> Read() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new DriveUsage(d.Name.TrimEnd('\\'), (ulong)d.TotalSize, (ulong)d.AvailableFreeSpace))
            .ToList();
}
