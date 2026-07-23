using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using RoundLaunchpad.Models;
using RoundLaunchpad.Services;

namespace RoundLaunchpad.Views;

public class LauncherWindow : Window
{
    private readonly SettingsStore _store;
    private readonly RingSession _session;
    private readonly Action _onDismiss;
    private readonly Action _onOpenSettings;
    private readonly Canvas _ringCanvas;
    private readonly CosmosBackground _cosmos;
    private readonly Grid _root;
    private readonly Dictionary<string, AppIconElement> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Ellipse _beam;
    private readonly RotateTransform _beamRotate = new();
    private readonly ScaleTransform _beamScale = new(1, 1);
    private readonly TextBlock _nameLabel;
    private readonly Border _emptyState;
    private HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _runningTimer;
    private DispatcherTimer? _beamFlickerTimer;
    private Point? _centerOverride;
    private double _orbitRadius;
    private double _ringExtent;
    private double _beamAngle = -Math.PI / 2;
    private Color _beamColor = Colors.White;
    private DateTime _flickerStart = DateTime.UtcNow;
    private string? _warpTargetId;
    private DateTime? _warpBegan;
    private bool _appeared;
    private string? _currentSelection;
    private double _lastHoverExit;
    private const double IconSize = 96;

    public LauncherWindow(SettingsStore store, RingSession session, Point? centerOverride,
        Action onDismiss, Action onOpenSettings)
    {
        _store = store;
        _session = session;
        _centerOverride = centerOverride;
        _onDismiss = onDismiss;
        _onOpenSettings = onOpenSettings;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = true;

        _cosmos = new CosmosBackground { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };

        _ringCanvas = new Canvas { IsHitTestVisible = true };

        _beam = new Ellipse
        {
            Width = 40,
            Height = 400,
            Opacity = 0,
            IsHitTestVisible = false,
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 1),
                EndPoint = new Point(0.5, 0),
                GradientStops =
                {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(Color.FromArgb(100, 255, 255, 255), 0.55),
                    new GradientStop(Color.FromArgb(200, 255, 255, 255), 1),
                }
            },
            Effect = new BlurEffect { Radius = 10 }
        };
        var beamOrigin = new TransformGroup();
        beamOrigin.Children.Add(new TranslateTransform(-20, -400));
        beamOrigin.Children.Add(_beamScale);
        beamOrigin.Children.Add(_beamRotate);
        _beam.RenderTransform = beamOrigin;
        _beam.RenderTransformOrigin = new Point(0.5, 1);

        _nameLabel = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(11, 5, 11, 5),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        var nameBorder = new Border
        {
            Child = _nameLabel,
            Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        _nameLabel.Tag = nameBorder;

        var chipBorder = new Border
        {
            Child = new TextBlock
            {
                Text = "Alt",
                FontSize = 18,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            Width = 64,
            Height = 48,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Tag = "chip"
        };

        _emptyState = BuildEmptyState();

        _root = new Grid();
        _root.Children.Add(_cosmos);

        var overlay = new Canvas { Name = "Overlay" };
        overlay.Children.Add(_ringCanvas);
        overlay.Children.Add(chipBorder);
        overlay.Children.Add(nameBorder);
        overlay.Children.Add(_emptyState);
        _root.Children.Add(overlay);

        // Click background to dismiss
        _cosmos.MouseLeftButtonDown += (_, _) => _onDismiss();

        Content = _root;

        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RingSession.SelectedId))
                OnSelectionChanged(_session.SelectedId);
            if (e.PropertyName == nameof(RingSession.LaunchRequestId) && _session.LaunchRequestId != null)
            {
                var app = _store.Apps.FirstOrDefault(a => a.Id == _session.LaunchRequestId);
                if (app != null) PerformWarpLaunch(app);
            }
        };

        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
        PreviewKeyDown += OnKeyDown;
        MouseMove += OnMouseMove;
        Deactivated += (_, _) => _onDismiss();
    }

    private Border BuildEmptyState()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(new TextBlock
        {
            Text = "No apps in your ring yet",
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        });
        var btn = new Button { Content = "Open Settings…", Padding = new Thickness(14, 8, 14, 8) };
        btn.Click += (_, _) => _onOpenSettings();
        panel.Children.Add(btn);
        return new Border
        {
            Child = panel,
            Padding = new Thickness(28),
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B)),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();
        Keyboard.Focus(this);
        RefreshRunning();
        BuildRing();
        _appeared = true;
        AnimateIn();

        _runningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _runningTimer.Tick += (_, _) => RefreshRunning();
        _runningTimer.Start();

        _beamFlickerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _beamFlickerTimer.Tick += (_, _) => UpdateBeamFlicker();
        _beamFlickerTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _runningTimer?.Stop();
        _beamFlickerTimer?.Stop();
        base.OnClosed(e);
    }

    private void AnimateIn()
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        _root.BeginAnimation(OpacityProperty, fade);
        _root.Opacity = 0;
        _root.BeginAnimation(OpacityProperty, fade);
    }

    private void BuildRing()
    {
        _ringCanvas.Children.Clear();
        _icons.Clear();

        var count = Math.Max(_store.Apps.Count, 1);
        _orbitRadius = Math.Max(250, count * (IconSize + 28) / (2 * Math.PI));
        _ringExtent = _orbitRadius + IconSize / 2 + 12;

        var center = GetCenter();
        Canvas.SetLeft(_beam, center.X);
        Canvas.SetTop(_beam, center.Y);
        _ringCanvas.Children.Add(_beam);

        if (_store.Apps.Count == 0)
        {
            _emptyState.Visibility = Visibility.Visible;
            PositionAt(_emptyState, center.X - 140, center.Y - 60);
            return;
        }

        _emptyState.Visibility = Visibility.Collapsed;

        for (int i = 0; i < _store.Apps.Count; i++)
        {
            var app = _store.Apps[i];
            var el = new AppIconElement(app, IconSize);
            el.MouseEnter += (_, _) =>
            {
                if (_warpTargetId != null) return;
                _session.SelectedId = app.Id;
            };
            el.MouseLeave += (_, _) =>
            {
                if (_warpTargetId != null) return;
                if (_session.SelectedId == app.Id)
                    _session.SelectedId = null;
            };
            el.MouseLeftButtonUp += (_, e) =>
            {
                if (_warpTargetId != null) return;
                e.Handled = true;
                PerformWarpLaunch(app);
            };
            _icons[app.Id] = el;
            _ringCanvas.Children.Add(el);
            PlaceIcon(el, i, center);
            el.SetRunning(AppLauncher.IsRunning(app.Path, _running));
        }

        // Option chip below ring
        if (_root.Children[1] is Canvas overlay)
        {
            foreach (var child in overlay.Children.OfType<Border>())
            {
                if (Equals(child.Tag, "chip"))
                {
                    Canvas.SetLeft(child, center.X - 32);
                    Canvas.SetTop(child, center.Y + _ringExtent + 46);
                }
            }
        }
    }

    private void PlaceIcon(AppIconElement el, int index, Point center)
    {
        var angle = AngleFor(index);
        var x = center.X + _orbitRadius * Math.Cos(angle) - IconSize / 2;
        var y = center.Y + _orbitRadius * Math.Sin(angle) - IconSize / 2;
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
    }

    private double AngleFor(int index) =>
        2 * Math.PI * index / Math.Max(_store.Apps.Count, 1) - Math.PI / 2;

    private Point GetCenter()
    {
        var w = ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth;
        var h = ActualHeight > 0 ? ActualHeight : SystemParameters.PrimaryScreenHeight;
        if (_centerOverride is Point o)
        {
            var margin = _ringExtent + 24;
            var bottomMargin = _ringExtent + 130;
            return new Point(
                Math.Clamp(o.X, margin, Math.Max(w - margin, margin)),
                Math.Clamp(o.Y, margin, Math.Max(h - bottomMargin, margin)));
        }
        return new Point(w / 2, h / 2 - 40);
    }

    private static void PositionAt(FrameworkElement el, double x, double y)
    {
        // empty state is in overlay canvas
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
    }

    private void OnSelectionChanged(string? newId)
    {
        var prev = _currentSelection;
        _currentSelection = newId;

        foreach (var kv in _icons)
            kv.Value.SetHovered(kv.Key == newId);

        var nameBorder = _nameLabel.Tag as Border;
        if (newId == null || !_store.Apps.Any(a => a.Id == newId))
        {
            _lastHoverExit = Environment.TickCount64 / 1000.0;
            _beam.Opacity = 0;
            if (nameBorder != null) nameBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var index = _store.Apps.ToList().FindIndex(a => a.Id == newId);
        if (index < 0) return;
        var app = _store.Apps[index];
        var target = AngleFor(index);
        var now = Environment.TickCount64 / 1000.0;
        var sweeping = prev != null || now - _lastHoverExit < 0.2;
        _beamColor = IconCache.GlowColor(app.Path);

        if (sweeping)
        {
            while (target - _beamAngle > Math.PI) target -= 2 * Math.PI;
            while (_beamAngle - target > Math.PI) target += 2 * Math.PI;
            // Beam defaults pointing up; Mac angle -π/2 is up → WPF degrees = rad·180/π + 90
            var fromDeg = _beamAngle * 180 / Math.PI + 90;
            var toDeg = target * 180 / Math.PI + 90;
            var anim = new DoubleAnimation(fromDeg, toDeg, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            _beamRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
            _beamAngle = target;
        }
        else
        {
            _beamRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            _beamRotate.Angle = target * 180 / Math.PI + 90;
            _beamAngle = target;
        }

        UpdateBeamBrush();
        _beam.Opacity = 1;

        // Name label
        _nameLabel.Text = ShortcutResolver.DisplayName(app.Path);
        if (nameBorder != null && _root.Children[1] is Canvas overlay)
        {
            nameBorder.Visibility = Visibility.Visible;
            nameBorder.Child = _nameLabel;
            if (!overlay.Children.Contains(nameBorder))
                overlay.Children.Add(nameBorder);
            var center = GetCenter();
            var labelRadius = _orbitRadius + IconSize / 2 + 34;
            var angle = AngleFor(index);
            // Measure
            _nameLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var sz = _nameLabel.DesiredSize;
            Canvas.SetLeft(nameBorder, center.X + labelRadius * Math.Cos(angle) - sz.Width / 2 - 11);
            Canvas.SetTop(nameBorder, center.Y + labelRadius * Math.Sin(angle) - sz.Height / 2 - 5);
        }
    }

    private void UpdateBeamBrush()
    {
        var c = _beamColor;
        _beam.Fill = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 1),
            EndPoint = new Point(0.5, 0),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0),
                new GradientStop(Color.FromArgb(110, c.R, c.G, c.B), 0.5),
                new GradientStop(Color.FromArgb(220, c.R, c.G, c.B), 1),
            }
        };
        // Scale beam length roughly to orbit
        _beam.Height = _orbitRadius;
        if (_beam.RenderTransform is TransformGroup tg && tg.Children[0] is TranslateTransform tt)
        {
            tt.Y = -_orbitRadius;
            tt.X = -20;
        }
    }

    private void UpdateBeamFlicker()
    {
        if (_session.SelectedId == null || _warpTargetId != null) return;
        var t = (DateTime.UtcNow - _flickerStart).TotalSeconds;
        var intensity = 0.94
            + 0.04 * Math.Sin(t * 9.3)
            + 0.03 * Math.Sin(t * 23.7 + 1.4)
            + 0.02 * Math.Sin(t * 41.1 + 4.0);

        const double slotLength = 2.8;
        var slot = (int)(t / slotLength);
        if (UnitRandom(slot, 12) < 0.6)
        {
            var dipStart = UnitRandom(slot, 13) * (slotLength - 0.25);
            var local = t - slot * slotLength - dipStart;
            const double dipDuration = 0.14;
            if (local >= 0 && local <= dipDuration)
            {
                var depth = 0.25 + 0.35 * UnitRandom(slot, 11);
                intensity -= depth * Math.Sin(local / dipDuration * Math.PI);
            }
        }
        intensity = Math.Max(0.3, intensity);
        _beam.Opacity = intensity;
        var widthScale = 1.0 + (intensity - 0.94) * 0.5;
        _beamScale.ScaleX = widthScale;
    }

    private static double UnitRandom(int seed, int salt)
    {
        unchecked
        {
            ulong h = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL;
            h += (ulong)(uint)salt * 0xBF58476D1CE4E5B9UL;
            h ^= h >> 30;
            h *= 0xBF58476D1CE4E5B9UL;
            h ^= h >> 27;
            h *= 0x94D049BB133111EBUL;
            h ^= h >> 31;
            return (h % 1_000_000UL) / 1_000_000.0;
        }
    }

    private void PerformWarpLaunch(LauncherApp app)
    {
        if (_warpTargetId != null) return;
        _session.SelectedId = app.Id;
        _warpTargetId = app.Id;
        _warpBegan = DateTime.UtcNow;
        _cosmos.WarpBegan = _warpBegan;

        foreach (var kv in _icons)
        {
            if (kv.Key == app.Id) kv.Value.SetWarpTarget();
            else kv.Value.SetWarpDimmed();
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(340) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            AppLauncher.LaunchOrActivate(app.Path);
            _onDismiss();
        };
        timer.Start();
    }

    private void RefreshRunning()
    {
        _running = AppLauncher.RunningExecutablePaths();
        foreach (var kv in _icons)
        {
            var app = _store.Apps.FirstOrDefault(a => a.Id == kv.Key);
            if (app != null)
                kv.Value.SetRunning(AppLauncher.IsRunning(app.Path, _running));
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var dx = (p.X - w / 2) / w;
        var dy = (p.Y - h / 2) / h;
        _cosmos.Parallax = new Vector(-dx * 26, -dy * 26);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_warpTargetId != null) { e.Handled = true; return; }

        switch (e.Key)
        {
            case Key.Escape:
                _onDismiss();
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Up:
                Step(-1);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Down:
                Step(1);
                e.Handled = true;
                break;
            case Key.Enter: // also Key.Return (same value in WPF)
                if (_session.LaunchRequestId == null && _session.SelectedId != null)
                    _session.LaunchRequestId = _session.SelectedId;
                e.Handled = true;
                break;
            default:
                if (_session.LaunchRequestId == null &&
                    e.Key >= Key.D1 && e.Key <= Key.D9)
                {
                    var digit = e.Key - Key.D0;
                    if (digit >= 1 && digit <= Math.Min(_store.Apps.Count, 9))
                    {
                        _session.LaunchRequestId = _store.Apps[digit - 1].Id;
                        e.Handled = true;
                    }
                }
                else if (_session.LaunchRequestId == null &&
                         e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9)
                {
                    var digit = e.Key - Key.NumPad0;
                    if (digit >= 1 && digit <= Math.Min(_store.Apps.Count, 9))
                    {
                        _session.LaunchRequestId = _store.Apps[digit - 1].Id;
                        e.Handled = true;
                    }
                }
                break;
        }
    }

    private void Step(int delta)
    {
        if (_store.Apps.Count == 0) return;
        var list = _store.Apps.ToList();
        if (_session.SelectedId != null)
        {
            var idx = list.FindIndex(a => a.Id == _session.SelectedId);
            if (idx >= 0)
            {
                var next = (idx + delta + list.Count) % list.Count;
                _session.SelectedId = list[next].Id;
                return;
            }
        }
        _session.SelectedId = list[delta >= 0 ? 0 : list.Count - 1].Id;
    }
}

internal sealed class AppIconElement : Grid
{
    private readonly Image _image;
    private readonly Ellipse _runningDot;
    private readonly DropShadowEffect _glow;
    private ScaleTransform _scale = new(1, 1);
    private bool _hovered;
    private Color _glowColor;

    public AppIconElement(LauncherApp app, double size)
    {
        Width = size;
        Height = size;
        _glowColor = IconCache.GlowColor(app.Path);
        _glow = new DropShadowEffect
        {
            Color = _glowColor,
            BlurRadius = 30,
            ShadowDepth = 0,
            Opacity = 0.7
        };
        Effect = _glow;

        _image = new Image
        {
            Source = IconCache.Icon(app.Path, (int)size),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform
        };
        RenderTransform = _scale;
        RenderTransformOrigin = new Point(0.5, 0.5);
        Children.Add(_image);

        _runningDot = new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, -16),
            Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect
            {
                Color = _glowColor,
                BlurRadius = 4,
                ShadowDepth = 0,
                Opacity = 0.9
            }
        };
        Children.Add(_runningDot);
        Cursor = Cursors.Hand;
    }

    public void SetHovered(bool hovered)
    {
        _hovered = hovered;
        var target = hovered ? 1.14 : 1.0;
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(160))
            { EasingFunction = new QuadraticEase() });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(160))
            { EasingFunction = new QuadraticEase() });
        _glow.BlurRadius = hovered ? 46 : 30;
        _glow.Opacity = hovered ? 0.95 : 0.7;
    }

    public void SetRunning(bool running) =>
        _runningDot.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

    public void SetWarpTarget()
    {
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1.5, TimeSpan.FromMilliseconds(280)));
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1.5, TimeSpan.FromMilliseconds(280)));
        _glow.BlurRadius = 50;
        _glow.Opacity = 1;
    }

    public void SetWarpDimmed()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0.12, TimeSpan.FromMilliseconds(280)));
    }
}
