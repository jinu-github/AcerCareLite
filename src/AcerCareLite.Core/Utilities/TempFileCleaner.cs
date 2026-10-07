namespace AcerCareLite.Core.Utilities;

/// <summary>
/// Cleans one folder tree. Safety rules: only files older than MinAge, only paths inside the root,
/// reparse points (junctions/symlinks) are never followed, files in use are skipped, nothing outside is touched.
/// </summary>
public sealed class TempFileCleaner : ITempCleaner
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
    };

    private readonly string _root;
    private readonly Func<DateTime> _utcNow;

    public TempFileCleaner(string root, TimeSpan? minAge = null, Func<DateTime>? utcNow = null)
    {
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var segments = _root.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4)
            throw new ArgumentException("Refusing to clean a folder this close to the drive root.", nameof(root));
        MinAge = minAge ?? TimeSpan.FromHours(24);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public TimeSpan MinAge { get; }

    public TempScanResult Scan()
    {
        long files = 0, bytes = 0;
        foreach (var f in EligibleFiles()) { files++; bytes += f.Length; }
        return new TempScanResult(files, bytes);
    }

    public TempCleanResult Clean()
    {
        long deleted = 0, freed = 0, skipped = 0;
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EligibleFiles().ToList())
        {
            try
            {
                var length = file.Length;
                var parent = file.DirectoryName;
                file.Delete();
                deleted++; freed += length;
                if (parent != null) touched.Add(parent);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
        }

        // Remove folders that are now empty (deepest first). Never the root itself.
        var cutoff = _utcNow() - MinAge;
        List<string> dirs;
        try { dirs = Directory.EnumerateDirectories(_root, "*", Options).OrderByDescending(d => d.Length).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { dirs = new List<string>(); }

        foreach (var dir in dirs)
        {
            try
            {
                if (!IsInsideRoot(dir)) continue;
                var info = new DirectoryInfo(dir);
                if (!touched.Contains(dir) && info.LastWriteTimeUtc >= cutoff) continue;
                if (info.EnumerateFileSystemInfos().Any()) continue;
                info.Delete(false);
                var parent = info.Parent?.FullName;
                if (parent != null) touched.Add(parent);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return new TempCleanResult(deleted, freed, skipped);
    }

    private IEnumerable<FileInfo> EligibleFiles()
    {
        if (!Directory.Exists(_root)) yield break;
        var cutoff = _utcNow() - MinAge;
        foreach (var path in Directory.EnumerateFiles(_root, "*", Options))
        {
            FileInfo info;
            try
            {
                if (!IsInsideRoot(path)) continue;
                info = new FileInfo(path);
                if (info.LastWriteTimeUtc >= cutoff) continue;
                _ = info.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            yield return info;
        }
    }

    private bool IsInsideRoot(string path) =>
        Path.GetFullPath(path).StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
