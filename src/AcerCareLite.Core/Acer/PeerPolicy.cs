using System.Text.RegularExpressions;

// Linked into the elevated helper as well: keep this file free of other Core dependencies.
namespace AcerCareLite.Core.Acer;

/// <summary>Pure rules for deciding whether the process at the other end of the pipe is the one we expect.</summary>
public static class PeerPolicy
{
    private static readonly Regex DriveRooted = new(@"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant);
    public const string DevelopmentHostFileName = "dotnet.exe";

    /// <summary>Full drive-letter path, no UNC, no relative paths, <c>..</c> resolved. Returns false for anything else.</summary>
    public static bool TryNormalize(string? path, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(path)) return false;

        var p = path.Trim();
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p[4..];
        if (!DriveRooted.IsMatch(p)) return false;

        try { normalized = Path.GetFullPath(p); }
        catch (Exception) { return false; }
        return DriveRooted.IsMatch(normalized);
    }

    public static bool PathsEqual(string? actual, string? expected)
    {
        if (!TryNormalize(actual, out var a) || !TryNormalize(expected, out var e)) return false;
        return string.Equals(a, e, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The peer is trusted when its image is exactly the expected file. In development builds only, the caller may
    /// additionally accept the .NET host (<c>dotnet.exe</c>); release builds pass false and get no bypass.
    /// </summary>
    public static bool IsTrustedPeer(string? actualImagePath, string expectedImagePath, bool allowDevelopmentHost)
    {
        if (PathsEqual(actualImagePath, expectedImagePath)) return true;
        if (!allowDevelopmentHost) return false;
        if (!TryNormalize(actualImagePath, out var actual)) return false;
        return string.Equals(Path.GetFileName(actual), DevelopmentHostFileName, StringComparison.OrdinalIgnoreCase);
    }
}
