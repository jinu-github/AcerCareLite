namespace AcerCareLite.Core.Acer;

/// <summary>One access-control entry, reduced to the facts the policy needs. RightsMask holds FileSystemRights bits.</summary>
public sealed record AceFact(string Sid, bool IsAllow, long RightsMask, bool InheritOnly);

/// <summary>A file or directory in the chain from the helper up to Program Files, with its owner and ACEs.</summary>
public sealed record SecuredItem(string Path, bool IsReparsePoint, string? OwnerSid, IReadOnlyList<AceFact> Aces);

public sealed record HelperLocationStatus(bool IsProtected, string Reason)
{
    public static HelperLocationStatus Protected(string reason) => new(true, reason);
    public static HelperLocationStatus NotProtected(string reason) => new(false, reason);
}

/// <summary>Whether the helper sits somewhere only administrators can change. Informational in 7b-0; mandatory before any write is enabled.</summary>
public interface IHelperLocationGate
{
    HelperLocationStatus Current { get; }
}

public static class ProtectedLocationPolicy
{
    // FileSystemRights bits that let an account change, replace or take over the object.
    // WriteData/CreateFiles, AppendData/CreateDirectories, WriteExtendedAttributes, DeleteSubdirectoriesAndFiles,
    // WriteAttributes, Delete, ChangePermissions, TakeOwnership, GenericAll, GenericWrite.
    public const long WriteLikeRights = 0x2L | 0x4L | 0x10L | 0x40L | 0x100L | 0x10000L | 0x40000L | 0x80000L | 0x10000000L | 0x40000000L;

    public static readonly IReadOnlySet<string> TrustedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "S-1-5-18",                                                          // SYSTEM
        "S-1-5-32-544",                                                      // BUILTIN\Administrators
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464",    // NT SERVICE\TrustedInstaller
        "S-1-3-0"                                                            // CREATOR OWNER (template entry)
    };

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsUnder(string root, string path) =>
        path.Length > root.Length && path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>The file, then every directory up to and including Program Files. Null when the file is not under Program Files.</summary>
    public static IReadOnlyList<string>? ChainPaths(string helperPath, string programFilesRoot)
    {
        if (string.IsNullOrWhiteSpace(helperPath) || string.IsNullOrWhiteSpace(programFilesRoot)) return null;

        string file, root;
        try { file = Normalize(helperPath); root = Normalize(programFilesRoot); }
        catch (Exception) { return null; }

        if (!IsUnder(root, file)) return null;

        var list = new List<string> { file };
        var dir = Path.GetDirectoryName(file);
        while (dir != null)
        {
            list.Add(dir);
            if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase)) return list;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    public static HelperLocationStatus Evaluate(string helperPath, string programFilesRoot, IReadOnlyList<SecuredItem> chain)
    {
        var expected = ChainPaths(helperPath, programFilesRoot);
        if (expected == null) return HelperLocationStatus.NotProtected("The helper is not under Program Files.");

        var byPath = new Dictionary<string, SecuredItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in chain)
        {
            try { byPath.TryAdd(Normalize(item.Path), item); }
            catch (Exception) { return HelperLocationStatus.NotProtected("A permissions entry had an invalid path."); }
        }

        foreach (var path in expected)
        {
            if (!byPath.TryGetValue(path, out var item))
                return HelperLocationStatus.NotProtected($"Permissions were not checked for {path}.");

            if (item.IsReparsePoint)
                return HelperLocationStatus.NotProtected($"{path} is a link.");

            if (item.OwnerSid == null || !TrustedSids.Contains(item.OwnerSid))
                return HelperLocationStatus.NotProtected($"{path} is not owned by an administrative account.");

            foreach (var ace in item.Aces)
            {
                if (!ace.IsAllow || ace.InheritOnly) continue;
                if ((ace.RightsMask & WriteLikeRights) == 0) continue;
                if (TrustedSids.Contains(ace.Sid)) continue;
                return HelperLocationStatus.NotProtected($"{path} can be modified by {ace.Sid}.");
            }
        }

        return HelperLocationStatus.Protected("Under Program Files; only administrative accounts can modify it.");
    }
}
