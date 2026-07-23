using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Reflection;

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
            var process = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(process) && File.Exists(process))
                return process;
        }
        catch { /* ignore */ }

        try
        {
            var loc = Assembly.GetExecutingAssembly().Location;
            if (loc.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = Path.ChangeExtension(loc, ".exe");
                if (File.Exists(candidate)) return candidate;
            }
            if (File.Exists(loc)) return loc;
        }
        catch { /* ignore */ }

        return Process.GetCurrentProcess().MainModule?.FileName ?? "";
    }
}
