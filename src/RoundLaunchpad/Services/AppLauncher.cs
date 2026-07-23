using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RoundLaunchpad.Services;

public static class AppLauncher
{
    public static void LaunchOrActivate(string path)
    {
        try
        {
            var target = path;
            string? args = null;
            string? workDir = null;

            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = ShortcutResolver.Resolve(path);
                if (!string.IsNullOrEmpty(resolved))
                    target = resolved!;
            }

            // Prefer activating an already-running instance when we can match the exe.
            if (TryActivateRunning(target))
                return;

            var psi = new ProcessStartInfo
            {
                FileName = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? path : target,
                UseShellExecute = true,
                WorkingDirectory = workDir ?? (File.Exists(target) ? Path.GetDirectoryName(target) : null) ?? ""
            };
            if (!string.IsNullOrEmpty(args))
                psi.Arguments = args;

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Launch failed: {ex.Message}");
        }
    }

    public static HashSet<string> RunningExecutablePaths()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path))
                        set.Add(path);
                }
                catch
                {
                    // Access denied for some system processes
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            // ignore
        }
        return set;
    }

    public static bool IsRunning(string appPath, HashSet<string>? running = null)
    {
        running ??= RunningExecutablePaths();
        var target = appPath;
        if (appPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            target = ShortcutResolver.Resolve(appPath) ?? appPath;

        if (running.Contains(target)) return true;

        // Match by file name as a softer signal
        try
        {
            var name = Path.GetFileName(target);
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var r in running)
            {
                if (string.Equals(Path.GetFileName(r), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }

    private static bool TryActivateRunning(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return false;
            var name = Path.GetFileNameWithoutExtension(exePath);
            var processes = Process.GetProcessesByName(name);
            foreach (var p in processes)
            {
                try
                {
                    string? path = null;
                    try { path = p.MainModule?.FileName; } catch { /* ignore */ }
                    if (path != null && !string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var hWnd = p.MainWindowHandle;
                    if (hWnd == IntPtr.Zero)
                    {
                        // Enumerate windows owned by this PID
                        hWnd = FindMainWindow(p.Id);
                    }
                    if (hWnd == IntPtr.Zero) continue;

                    if (IsIconic(hWnd))
                        ShowWindow(hWnd, SW_RESTORE);
                    SetForegroundWindow(hWnd);
                    return true;
                }
                catch
                {
                    // try next
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            // ignore
        }
        return false;
    }

    private static IntPtr FindMainWindow(int pid)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out var windowPid);
            if (windowPid != pid) return true;
            if (!IsWindowVisible(hWnd)) return true;
            var length = GetWindowTextLength(hWnd);
            if (length == 0) return true;
            result = hWnd;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    private const int SW_RESTORE = 9;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}
