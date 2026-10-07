using System.Text;
using System.Text.Json;

namespace AcerCareLite.Core.Acer;

/// <summary>JSON-lines audit log of every change attempt, written by the (unelevated) app. Rotated once at about 1 MB.</summary>
public sealed class FileAcerWriteAuditLog : IAcerWriteAuditLog
{
    public const long MaxBytes = 1_000_000;

    private readonly string _path;
    private readonly object _lock = new();

    public FileAcerWriteAuditLog() : this(DefaultPath()) { }

    public FileAcerWriteAuditLog(string path) => _path = path;

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AcerCareLite", "acer-write-audit.log");

    public bool Append(AcerWriteAuditEntry entry)
    {
        try
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var info = new FileInfo(_path);
                if (info.Exists && info.Length >= MaxBytes) File.Move(_path, _path + ".1", overwrite: true);

                File.AppendAllText(_path, JsonSerializer.Serialize(entry) + "\n", new UTF8Encoding(false));
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
