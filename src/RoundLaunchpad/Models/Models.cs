using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace RoundLaunchpad.Models;

public sealed class LauncherApp
{
    public string Path { get; init; } = "";

    [JsonIgnore]
    public string Id => Path;

    [JsonIgnore]
    public string Name
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path)) return "";
            try
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(Path);
                if (Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    name = System.IO.Path.GetFileNameWithoutExtension(Path);
                return string.IsNullOrWhiteSpace(name) ? Path : name;
            }
            catch
            {
                return Path;
            }
        }
    }
}

public sealed class AppConfig
{
    public List<string> Apps { get; set; } = new();
    public bool DoubleTapAlt { get; set; }
    public bool OpenAtMouse { get; set; }
    public bool LaunchAtLogin { get; set; }
}

/// <summary>Shared selection state for one showing of the ring.</summary>
public sealed class RingSession : System.ComponentModel.INotifyPropertyChanged
{
    private string? _selectedId;
    private string? _launchRequestId;

    public string? SelectedId
    {
        get => _selectedId;
        set
        {
            if (_selectedId == value) return;
            _selectedId = value;
            PropertyChanged?.Invoke(this, new(nameof(SelectedId)));
        }
    }

    public string? LaunchRequestId
    {
        get => _launchRequestId;
        set
        {
            if (_launchRequestId == value) return;
            _launchRequestId = value;
            PropertyChanged?.Invoke(this, new(nameof(LaunchRequestId)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public static class ConfigPaths
{
    public static string AppDataDir
    {
        get
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RoundLaunchpad");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConfigFile => System.IO.Path.Combine(AppDataDir, "config.json");
}

public static class JsonOpts
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
