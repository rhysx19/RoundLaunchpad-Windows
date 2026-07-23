using System.IO;
using System.Text.Json;
using System.Windows;
using RoundLaunchpad.Models;

namespace RoundLaunchpad.Services;

public sealed class SettingsStore : System.ComponentModel.INotifyPropertyChanged
{
    private readonly List<LauncherApp> _apps = new();
    private bool _doubleTapAlt;
    private bool _openAtMouse;
    private bool _launchAtLogin;
    private bool _loading = true;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public event Action? ActivationChanged;

    public IReadOnlyList<LauncherApp> Apps => _apps;

    public bool DoubleTapAlt
    {
        get => _doubleTapAlt;
        set
        {
            if (_doubleTapAlt == value) return;
            _doubleTapAlt = value;
            Notify(nameof(DoubleTapAlt));
            Save();
            ActivationChanged?.Invoke();
        }
    }

    public bool OpenAtMouse
    {
        get => _openAtMouse;
        set
        {
            if (_openAtMouse == value) return;
            _openAtMouse = value;
            Notify(nameof(OpenAtMouse));
            Save();
        }
    }

    public bool LaunchAtLogin
    {
        get => _launchAtLogin;
        set
        {
            if (_launchAtLogin == value) return;
            _launchAtLogin = value;
            Notify(nameof(LaunchAtLogin));
            LoginItem.SetEnabled(value);
            Save();
        }
    }

    public SettingsStore()
    {
        Load();
        _loading = false;
    }

    public void ReloadFromDisk()
    {
        _loading = true;
        Load();
        _loading = false;
        Notify(nameof(Apps));
        Notify(nameof(DoubleTapAlt));
        Notify(nameof(OpenAtMouse));
        Notify(nameof(LaunchAtLogin));
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(_apps.Select(a => a.Path), StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var full = Path.GetFullPath(p);
            if (!existing.Add(full)) continue;
            if (!File.Exists(full) && !Directory.Exists(full)) continue;
            _apps.Add(new LauncherApp { Path = full });
            changed = true;
        }
        if (changed)
        {
            Notify(nameof(Apps));
            Save();
        }
    }

    public void Remove(LauncherApp app)
    {
        if (_apps.RemoveAll(a => string.Equals(a.Path, app.Path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Notify(nameof(Apps));
            Save();
        }
    }

    public void Move(int from, int to)
    {
        if (from < 0 || from >= _apps.Count || to < 0 || to >= _apps.Count || from == to) return;
        var item = _apps[from];
        _apps.RemoveAt(from);
        _apps.Insert(to, item);
        Notify(nameof(Apps));
        Save();
    }

    public void ReplaceApps(IEnumerable<LauncherApp> apps)
    {
        _apps.Clear();
        _apps.AddRange(apps);
        Notify(nameof(Apps));
        Save();
    }

    private void Load()
    {
        _apps.Clear();
        try
        {
            if (File.Exists(ConfigPaths.ConfigFile))
            {
                var json = File.ReadAllText(ConfigPaths.ConfigFile);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts.Options);
                if (cfg != null)
                {
                    foreach (var p in cfg.Apps)
                    {
                        if (File.Exists(p) || Directory.Exists(p))
                            _apps.Add(new LauncherApp { Path = p });
                    }
                    _doubleTapAlt = cfg.DoubleTapAlt;
                    _openAtMouse = cfg.OpenAtMouse;
                    _launchAtLogin = cfg.LaunchAtLogin;
                    // Keep registry in sync with stored preference
                    LoginItem.SetEnabled(_launchAtLogin);
                    return;
                }
            }
        }
        catch
        {
            // fall through to defaults
        }

        foreach (var p in DefaultApps())
            _apps.Add(new LauncherApp { Path = p });
        _launchAtLogin = LoginItem.IsEnabled();
    }

    private void Save()
    {
        if (_loading) return;
        try
        {
            var cfg = new AppConfig
            {
                Apps = _apps.Select(a => a.Path).ToList(),
                DoubleTapAlt = _doubleTapAlt,
                OpenAtMouse = _openAtMouse,
                LaunchAtLogin = _launchAtLogin
            };
            File.WriteAllText(ConfigPaths.ConfigFile, JsonSerializer.Serialize(cfg, JsonOpts.Options));
        }
        catch
        {
            // ignore write failures
        }
    }

    private static IEnumerable<string> DefaultApps()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) is { Length: > 0 } x86
                ? x86 : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "calc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "SnippingTool.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "microsoft.windowsstore_8wekyb3d8bbwe", "WinStore.App.exe"),
            ResolveStartMenu("Microsoft Edge.lnk"),
            ResolveStartMenu("Google Chrome.lnk"),
            ResolveStartMenu("Firefox.lnk"),
            ResolveStartMenu("Spotify.lnk"),
            ResolveStartMenu("Discord.lnk"),
            ResolveStartMenu("Visual Studio Code.lnk"),
            ResolveStartMenu("Notepad.lnk"),
            ResolveStartMenu("Calculator.lnk"),
            ResolveStartMenu("Photos.lnk"),
            ResolveStartMenu("Mail.lnk"),
            ResolveStartMenu("Windows Terminal.lnk"),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            if (!File.Exists(c) && !Directory.Exists(c)) continue;
            if (!seen.Add(c)) continue;
            yield return c;
            if (seen.Count >= 8) yield break;
        }
    }

    private static string? ResolveStartMenu(string shortcutName)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu"),
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                var hit = Directory.EnumerateFiles(root, shortcutName, SearchOption.AllDirectories).FirstOrDefault();
                if (hit != null) return hit;
            }
            catch
            {
                // access denied on some folders
            }
        }
        return null;
    }

    private void Notify(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
