using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using RoundLaunchpad.Models;
using RoundLaunchpad.Services;
using RoundLaunchpad.Views;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace RoundLaunchpad;

/// <summary>
/// Owns tray icon, hotkeys, launcher overlay, and settings — Mac AppDelegate equivalent.
/// </summary>
public sealed class AppController : IDisposable
{
    private readonly SettingsStore _store = new();
    private readonly Forms.NotifyIcon _tray;
    private readonly Window _messageWindow;
    private HotkeyService? _hotkey;
    private LauncherWindow? _launcher;
    private RingSession? _session;
    private SettingsWindow? _settings;
    private DateTime _shownAt;
    private bool _disposed;

    public AppController()
    {
        // Hidden window so we have an HWND for RegisterHotKey.
        _messageWindow = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Opacity = 0,
            Title = "RoundLaunchpad"
        };
        _messageWindow.Show();
        _messageWindow.Hide();

        _tray = new Forms.NotifyIcon
        {
            Visible = true,
            Text = "RoundLaunchpad",
            Icon = LoadTrayIcon()
        };
        _tray.DoubleClick += (_, _) => ToggleLauncher();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Launcher (Alt+Space)", null, (_, _) => ToggleLauncher());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit RoundLaunchpad", null, (_, _) => Quit());
        _tray.ContextMenuStrip = menu;
    }

    public void Start()
    {
        var hwnd = new WindowInteropHelper(_messageWindow).EnsureHandle();
        _hotkey = new HotkeyService(
            onDown: () => Application.Current.Dispatcher.Invoke(ToggleLauncher),
            onUp: () => Application.Current.Dispatcher.Invoke(HotkeyReleased),
            onDoubleTapAlt: () => Application.Current.Dispatcher.Invoke(ToggleLauncher));
        _hotkey.Attach(hwnd);
        _hotkey.SetDoubleTapAlt(_store.DoubleTapAlt);

        _store.ActivationChanged += () => _hotkey?.SetDoubleTapAlt(_store.DoubleTapAlt);
    }

    private void ToggleLauncher()
    {
        if (_launcher != null) HideLauncher();
        else ShowLauncher();
    }

    private void ShowLauncher()
    {
        if (_launcher != null) return;

        Point? center = null;
        if (_store.OpenAtMouse)
        {
            var mouse = Forms.Control.MousePosition;
            // Which screen contains the mouse?
            var screen = Forms.Screen.FromPoint(mouse);
            // Position window on that screen and convert mouse to window coords (top-left origin).
            _launcher = CreateLauncherWindow(screen, new Point(mouse.X - screen.Bounds.Left, mouse.Y - screen.Bounds.Top));
        }
        else
        {
            var screen = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
            _launcher = CreateLauncherWindow(screen, null);
        }

        _shownAt = DateTime.UtcNow;
        _launcher.Show();
        _launcher.Activate();
    }

    private LauncherWindow CreateLauncherWindow(Forms.Screen screen, Point? centerOverride)
    {
        _session = new RingSession();
        var win = new LauncherWindow(
            _store,
            _session,
            centerOverride,
            onDismiss: HideLauncher,
            onOpenSettings: () =>
            {
                HideLauncher();
                ShowSettings();
            })
        {
            Left = screen.Bounds.Left,
            Top = screen.Bounds.Top,
            Width = screen.Bounds.Width,
            Height = screen.Bounds.Height
        };
        return win;
    }

    private void HideLauncher()
    {
        if (_launcher == null) return;
        try
        {
            _launcher.Close();
        }
        catch { /* ignore */ }
        _launcher = null;
        _session = null;
    }

    private void HotkeyReleased()
    {
        if (_launcher == null || _session == null) return;
        if ((DateTime.UtcNow - _shownAt).TotalSeconds <= 0.3) return;
        if (_session.LaunchRequestId != null) return;
        if (_session.SelectedId == null) return;
        _session.LaunchRequestId = _session.SelectedId;
    }

    private void ShowSettings()
    {
        HideLauncher();
        if (_settings == null)
        {
            _settings = new SettingsWindow(_store);
            _settings.Closed += (_, _) => _settings = null;
        }
        _settings.Show();
        _settings.Activate();
    }

    private void Quit()
    {
        _tray.Visible = false;
        Application.Current.Shutdown();
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var icoPath = Path.Combine(baseDir, "Assets", "AppIcon.ico");
            if (File.Exists(icoPath))
                return new Icon(icoPath, 16, 16);

            // Embedded resource fallback — draw ring of dots
            return CreateRingIcon();
        }
        catch
        {
            return CreateRingIcon();
        }
    }

    private static Icon CreateRingIcon()
    {
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(System.Drawing.Color.White);
            double cx = 8, cy = 8, orbit = 5.2, r = 1.4;
            for (int i = 0; i < 8; i++)
            {
                var a = i / 8.0 * Math.PI * 2;
                var x = cx + orbit * Math.Cos(a);
                var y = cy + orbit * Math.Sin(a);
                g.FillEllipse(brush, (float)(x - r), (float)(y - r), (float)(r * 2), (float)(r * 2));
            }
        }
        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        HideLauncher();
        _hotkey?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        try { _messageWindow.Close(); } catch { /* ignore */ }
    }
}
