using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace RoundLaunchpad.Services;

/// <summary>HKCU Run key for launch-at-login (Mac SMAppService equivalent).</summary>
public static class LoginItem
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RoundLaunchpad";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ??
                            Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) return;
            if (enabled)
            {
                var exe = GetExecutablePath();
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch
        {
            // ignore
        }
    }

    public static string GetExecutablePath()
    {
        try
        {
            // Single-file publish: Location is empty; prefer process path / base dir.
            var process = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(process) && File.Exists(process))
                return process;

            var baseDir = AppContext.BaseDirectory;
            var candidate = Path.Combine(baseDir, "RoundLaunchpad.exe");
            if (File.Exists(candidate)) return candidate;
        }
        catch { /* ignore */ }

        return Process.GetCurrentProcess().MainModule?.FileName ?? "";
    }
}
