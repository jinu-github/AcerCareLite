using System.Security.AccessControl;
using System.Security.Principal;
using AcerCareLite.Core.Acer;

namespace AcerCareLite.Acer;

/// <summary>Reads the real owner/ACLs from a file up to Program Files and hands them to the pure policy. Read-only. Also linked into the elevated helper.</summary>
public sealed class HelperLocationGate : IHelperLocationGate
{
    private readonly string _helperPath;
    private readonly Lazy<HelperLocationStatus> _status;

    public HelperLocationGate(string helperPath)
    {
        _helperPath = helperPath;
        _status = new Lazy<HelperLocationStatus>(Compute);
    }

    public HelperLocationStatus Current => _status.Value;

    private HelperLocationStatus Compute()
    {
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var paths = ProtectedLocationPolicy.ChainPaths(_helperPath, root);
            if (paths == null) return HelperLocationStatus.NotProtected("The helper is not under Program Files (development build).");
            if (!File.Exists(_helperPath)) return HelperLocationStatus.NotProtected("The helper file was not found.");

            var items = paths.Select(Read).ToList();
            return ProtectedLocationPolicy.Evaluate(_helperPath, root, items);
        }
        catch (Exception ex)
        {
            return HelperLocationStatus.NotProtected("Permissions could not be read: " + ex.Message);
        }
    }

    private static SecuredItem Read(string path)
    {
        var attributes = File.GetAttributes(path);
        var reparse = (attributes & FileAttributes.ReparsePoint) != 0;
        FileSystemSecurity security = (attributes & FileAttributes.Directory) != 0
            ? new DirectoryInfo(path).GetAccessControl()
            : new FileInfo(path).GetAccessControl();

        var owner = (security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier)?.Value;
        var aces = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => new AceFact(
                rule.IdentityReference.Value,
                rule.AccessControlType == AccessControlType.Allow,
                unchecked((uint)(int)rule.FileSystemRights),
                (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0))
            .ToList();

        return new SecuredItem(path, reparse, owner, aces);
    }
}
