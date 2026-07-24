using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private readonly Canvas _beamLayer;
    private readonly Path _beamCore;
    private readonly Path _beamSoft;
    private readonly Path _beamHalo;
    private readonly TextBlock _nameLabel;
    private readonly Border _emptyState;
    private HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _runningTimer;
    private DispatcherTimer? _beamTimer;
    private Point? _centerOverride;
    private double _orbitRadius;
    private double _ringExtent;
    private double _beamAngle = -Math.PI / 2;
    private double _beamDisplayAngle = -Math.PI / 2;
    private double _beamTargetAngle = -Math.PI / 2;
    private bool _beamAnimating;
    private DateTime _beamAnimStart;
    private double _beamAnimFrom;
    private double _beamAnimTo;
    private Color _beamColor = Colors.White;
    private DateTime _flickerStart = DateTime.UtcNow;
    private string? _warpTargetId;
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

        _cosmos = new CosmosBackground
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _ringCanvas = new Canvas { IsHitTestVisible = true };

        // Beam layers: wide halo + soft mid + brighter core (no BlurEffect —
        // effects often fail on AllowsTransparency windows).
        _beamHalo = MakeBeamPath(isHitTestVisible: false);
        _beamSoft = MakeBeamPath(isHitTestVisible: false);
        _beamCore = MakeBeamPath(isHitTestVisible: false);
        _beamLayer = new Canvas { IsHitTestVisible = false, Opacity = 0 };
        _beamLayer.Children.Add(_beamHalo);
        _beamLayer.Children.Add(_beamSoft);
        _beamLayer.Children.Add(_beamCore);

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

    private static Path MakeBeamPath(bool isHitTestVisible) => new()
    {
        IsHitTestVisible = isHitTestVisible,
        Stretch = Stretch.None,
        StrokeThickness = 0
    };

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
        AnimateIn();

        _runningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _runningTimer.Tick += (_, _) => RefreshRunning();
        _runningTimer.Start();

        // ~30 fps: beam flicker + angle lerp (matches Mac TimelineView)
        _beamTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _beamTimer.Tick += (_, _) => TickBeam();
        _beamTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _runningTimer?.Stop();
        _beamTimer?.Stop();
        base.OnClosed(e);
    }

    private void AnimateIn()
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
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
        _ringCanvas.Children.Add(_beamLayer);

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

        // Initial beam geometry (hidden until selection)
        RedrawBeam(_beamDisplayAngle, intensity: 0.9, widthFactor: 1.0);
    }

    private void PlaceIcon(AppIconElement el, int index, Point center)
    {
        var angle = AngleFor(index);
        // Element is larger than the icon to leave room for the glow.
        var box = el.BoxSize;
        var x = center.X + _orbitRadius * Math.Cos(angle) - box / 2;
        var y = center.Y + _orbitRadius * Math.Sin(angle) - box / 2;
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
            _beamLayer.Opacity = 0;
            _beamAnimating = false;
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

        // Shortest path around the circle (same as Mac).
        while (target - _beamAngle > Math.PI) target -= 2 * Math.PI;
        while (_beamAngle - target > Math.PI) target += 2 * Math.PI;

        if (sweeping)
        {
            _beamAnimFrom = _beamDisplayAngle;
            // Normalize display angle near target for smooth lerp
            while (_beamAnimFrom - target > Math.PI) _beamAnimFrom -= 2 * Math.PI;
            while (target - _beamAnimFrom > Math.PI) _beamAnimFrom += 2 * Math.PI;
            _beamAnimTo = target;
            _beamAnimStart = DateTime.UtcNow;
            _beamAnimating = true;
        }
        else
        {
            _beamAnimating = false;
            _beamDisplayAngle = target;
        }
        _beamAngle = target;
        _beamTargetAngle = target;
        _beamLayer.Opacity = 1;

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
            _nameLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var sz = _nameLabel.DesiredSize;
            Canvas.SetLeft(nameBorder, center.X + labelRadius * Math.Cos(angle) - sz.Width / 2 - 11);
            Canvas.SetTop(nameBorder, center.Y + labelRadius * Math.Sin(angle) - sz.Height / 2 - 5);
        }
    }

    private void TickBeam()
    {
        if (_session.SelectedId == null || _warpTargetId != null)
            return;

        if (_beamAnimating)
        {
            var t = (DateTime.UtcNow - _beamAnimStart).TotalMilliseconds / 280.0;
            if (t >= 1)
            {
                _beamDisplayAngle = _beamAnimTo;
                _beamAnimating = false;
            }
            else
            {
                // Ease-out quadratic
                var e = 1 - (1 - t) * (1 - t);
                _beamDisplayAngle = _beamAnimFrom + (_beamAnimTo - _beamAnimFrom) * e;
            }
        }

        var flicker = BeamFlicker((DateTime.UtcNow - _flickerStart).TotalSeconds);
        RedrawBeam(_beamDisplayAngle, flicker.intensity, flicker.width);
        _beamLayer.Opacity = Math.Clamp(flicker.intensity, 0.35, 1.0);
    }

    /// <summary>
    /// Mac BeamShape: tapered trapezoid from center → icon, plus wider soft copies
    /// so it reads as a thick light beam without relying on BlurEffect.
    /// </summary>
    private void RedrawBeam(double angle, double intensity, double widthFactor)
    {
        var center = GetCenter();
        var length = Math.Max(_orbitRadius, 1);
        // Mac: startWidth 8, endWidth iconSize * 0.9 * flicker.width
        var endW = IconSize * 0.9 * widthFactor;

        // Three layers: huge soft halo, medium soft, bright core
        ApplyTrapezoid(_beamHalo, center, angle, length,
            startWidth: 28, endWidth: endW * 1.85,
            color: _beamColor, opacityScale: 0.18 * intensity);

        ApplyTrapezoid(_beamSoft, center, angle, length,
            startWidth: 14, endWidth: endW * 1.25,
            color: _beamColor, opacityScale: 0.40 * intensity);

        ApplyTrapezoid(_beamCore, center, angle, length,
            startWidth: 8, endWidth: endW,
            color: _beamColor, opacityScale: 0.85 * intensity);
    }

    private static void ApplyTrapezoid(Path path, Point center, double angle, double length,
        double startWidth, double endWidth, Color color, double opacityScale)
    {
        var dirX = Math.Cos(angle);
        var dirY = Math.Sin(angle);
        var perpX = -dirY;
        var perpY = dirX;

        var end = new Point(center.X + dirX * length, center.Y + dirY * length);

        var p0 = new Point(center.X + perpX * startWidth / 2, center.Y + perpY * startWidth / 2);
        var p1 = new Point(end.X + perpX * endWidth / 2, end.Y + perpY * endWidth / 2);
        var p2 = new Point(end.X - perpX * endWidth / 2, end.Y - perpY * endWidth / 2);
        var p3 = new Point(center.X - perpX * startWidth / 2, center.Y - perpY * startWidth / 2);

        var fig = new PathFigure { StartPoint = p0, IsClosed = true, IsFilled = true };
        fig.Segments.Add(new LineSegment(p1, true));
        fig.Segments.Add(new LineSegment(p2, true));
        fig.Segments.Add(new LineSegment(p3, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        path.Data = geo;

        // Fade from transparent at the hub to bright near the icon (Mac RadialGradient).
        var a0 = (byte)0;
        var a1 = (byte)Math.Clamp(opacityScale * 0.45 * 255, 0, 255);
        var a2 = (byte)Math.Clamp(opacityScale * 255, 0, 255);

        path.Fill = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = center,
            EndPoint = end,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(a0, color.R, color.G, color.B), 0.0),
                new GradientStop(Color.FromArgb(a1, color.R, color.G, color.B), 0.45),
                new GradientStop(Color.FromArgb(a2, color.R, color.G, color.B), 1.0),
            }
        };
    }

    private (double intensity, double width) BeamFlicker(double t)
    {
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
        var width = 1.0 + (intensity - 0.94) * 0.5;
        return (intensity, width);
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
        _cosmos.WarpBegan = DateTime.UtcNow;

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

/// <summary>
/// Icon + soft radial glow. Glow is painted as gradient ellipses (not DropShadowEffect),
/// because bitmap effects are unreliable / invisible on transparent WPF windows.
/// The element is larger than the icon so the glow isn't clipped.
/// </summary>
internal sealed class AppIconElement : Canvas
{
    private readonly Image _image;
    private readonly Ellipse _glowOuter;
    private readonly Ellipse _glowInner;
    private readonly Ellipse _runningDot;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Color _glowColor;
    private readonly double _iconSize;
    public double BoxSize { get; }

    public AppIconElement(LauncherApp app, double size)
    {
        _iconSize = size;
        BoxSize = size * 2.4; // room for glow halo
        Width = BoxSize;
        Height = BoxSize;
        ClipToBounds = false;

        _glowColor = IconCache.GlowColor(app.Path);

        // Outer soft bloom
        _glowOuter = new Ellipse
        {
            Width = size * 2.1,
            Height = size * 2.1,
            IsHitTestVisible = false,
            Fill = MakeGlowBrush(_glowColor, peakAlpha: 90)
        };
        // Inner brighter core glow
        _glowInner = new Ellipse
        {
            Width = size * 1.45,
            Height = size * 1.45,
            IsHitTestVisible = false,
            Fill = MakeGlowBrush(_glowColor, peakAlpha: 160)
        };

        _image = new Image
        {
            Source = IconCache.GetIcon(app.Path, (int)size),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = true
        };

        _runningDot = new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };

        Children.Add(_glowOuter);
        Children.Add(_glowInner);
        Children.Add(_image);
        Children.Add(_runningDot);

        LayoutChildren(hovered: false);

        // Scale around icon center
        var tg = new TransformGroup();
        tg.Children.Add(_scale);
        RenderTransform = tg;
        RenderTransformOrigin = new Point(0.5, 0.5);

        Cursor = Cursors.Hand;
        Background = Brushes.Transparent; // hit-test the whole box lightly via image only
    }

    private void LayoutChildren(bool hovered)
    {
        var box = BoxSize;
        var icon = _iconSize;
        var glowScale = hovered ? 1.15 : 1.0;

        PlaceCentered(_glowOuter, box, _glowOuter.Width * glowScale, _glowOuter.Height * glowScale);
        // Update size for hover bloom
        _glowOuter.Width = _iconSize * 2.1 * glowScale;
        _glowOuter.Height = _iconSize * 2.1 * glowScale;
        _glowInner.Width = _iconSize * 1.45 * (hovered ? 1.12 : 1.0);
        _glowInner.Height = _iconSize * 1.45 * (hovered ? 1.12 : 1.0);
        PlaceCentered(_glowOuter, box, _glowOuter.Width, _glowOuter.Height);
        PlaceCentered(_glowInner, box, _glowInner.Width, _glowInner.Height);
        PlaceCentered(_image, box, icon, icon);

        Canvas.SetLeft(_runningDot, (box - 6) / 2);
        Canvas.SetTop(_runningDot, box / 2 + icon / 2 + 8);
    }

    private static void PlaceCentered(FrameworkElement el, double box, double w, double h)
    {
        Canvas.SetLeft(el, (box - w) / 2);
        Canvas.SetTop(el, (box - h) / 2);
    }

    private static RadialGradientBrush MakeGlowBrush(Color c, byte peakAlpha)
    {
        return new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(peakAlpha, c.R, c.G, c.B), 0.0),
                new GradientStop(Color.FromArgb((byte)(peakAlpha * 0.55), c.R, c.G, c.B), 0.35),
                new GradientStop(Color.FromArgb((byte)(peakAlpha * 0.18), c.R, c.G, c.B), 0.65),
                new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1.0),
            }
        };
    }

    public void SetHovered(bool hovered)
    {
        var target = hovered ? 1.14 : 1.0;
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(160))
            { EasingFunction = new QuadraticEase() });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(160))
            { EasingFunction = new QuadraticEase() });

        var peakOuter = hovered ? (byte)140 : (byte)90;
        var peakInner = hovered ? (byte)210 : (byte)160;
        _glowOuter.Fill = MakeGlowBrush(_glowColor, peakOuter);
        _glowInner.Fill = MakeGlowBrush(_glowColor, peakInner);
        LayoutChildren(hovered);
    }

    public void SetRunning(bool running) =>
        _runningDot.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

    public void SetWarpTarget()
    {
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(1.5, TimeSpan.FromMilliseconds(280)));
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(1.5, TimeSpan.FromMilliseconds(280)));
        _glowOuter.Fill = MakeGlowBrush(_glowColor, 220);
        _glowInner.Fill = MakeGlowBrush(_glowColor, 255);
        LayoutChildren(hovered: true);
    }

    public void SetWarpDimmed()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0.12, TimeSpan.FromMilliseconds(280)));
    }
}
