using AcerCareLite.Core.Settings;
using Microsoft.Win32;

namespace AcerCareLite.Windows.Monitoring;

/// <summary>This app's own entry in the current user's Run key. Nothing else in that key is touched.</summary>
public sealed class AutoStartService : IAutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AcerCareLite";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch { return false; }
    }

    public bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                var path = Environment.ProcessPath;
                // Never register the dotnet host itself (happens when started with "dotnet AcerCareLite.dll").
                if (path == null || Path.GetFileName(path).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return false;
                key.SetValue(ValueName, $"\"{path}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch { return false; }
    }
}
