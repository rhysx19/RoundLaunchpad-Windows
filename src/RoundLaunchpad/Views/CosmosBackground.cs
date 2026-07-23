using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace RoundLaunchpad.Views;

/// <summary>
/// Deep-space backdrop: gradient sky, nebulae, twinkling stars, planets, meteors.
/// Mirrors the Mac CosmosBackground behavior at ~30 fps.
/// </summary>
public sealed class CosmosBackground : FrameworkElement
{
    public static readonly DependencyProperty ParallaxProperty =
        DependencyProperty.Register(nameof(Parallax), typeof(Vector), typeof(CosmosBackground),
            new FrameworkPropertyMetadata(new Vector(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WarpBeganProperty =
        DependencyProperty.Register(nameof(WarpBegan), typeof(DateTime?), typeof(CosmosBackground),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Vector Parallax
    {
        get => (Vector)GetValue(ParallaxProperty);
        set => SetValue(ParallaxProperty, value);
    }

    public DateTime? WarpBegan
    {
        get => (DateTime?)GetValue(WarpBeganProperty);
        set => SetValue(WarpBeganProperty, value);
    }

    private readonly Star[] _stars;
    private readonly DateTime _start = DateTime.UtcNow;
    private readonly DispatcherTimer _timer;

    private static readonly Color[] StarTints =
    {
        Colors.White,
        Color.FromRgb(0xCF, 0xE0, 0xFF),
        Color.FromRgb(0xFF, 0xEB, 0xC7),
    };

    public CosmosBackground()
    {
        _stars = Star.MakeField(170);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30.0) };
        _timer.Tick += (_, _) => InvalidateVisual();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;

        var t = (DateTime.UtcNow - _start).TotalSeconds;
        double? warpElapsed = WarpBegan is DateTime wb
            ? (DateTime.UtcNow - wb).TotalSeconds
            : null;

        DrawSky(dc, size);
        DrawNebulae(dc, size);
        DrawStars(dc, size, t, warpElapsed);
        DrawPlanets(dc, size);
        DrawMeteor(dc, size, t);
    }

    private static void DrawSky(DrawingContext dc, Size size)
    {
        var brush = new LinearGradientBrush(
            Color.FromRgb(0x04, 0x04, 0x0D),
            Color.FromRgb(0x0D, 0x08, 0x1A),
            90);
        dc.DrawRectangle(brush, null, new Rect(size));
    }

    private static void DrawNebulae(DrawingContext dc, Size size)
    {
        Nebula(dc, new Point(size.Width * 0.72, size.Height * 0.25), size.Width * 0.30,
            Color.FromRgb(0x73, 0x40, 0xBF), 0.10);
        Nebula(dc, new Point(size.Width * 0.18, size.Height * 0.78), size.Width * 0.26,
            Color.FromRgb(0x26, 0x8C, 0xA6), 0.08);
    }

    private static void Nebula(DrawingContext dc, Point center, double radius, Color color, double opacity)
    {
        var brush = new RadialGradientBrush(
            Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B),
            Color.FromArgb(0, color.R, color.G, color.B))
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        dc.DrawEllipse(brush, null, center, radius, radius);
    }

    private void DrawStars(DrawingContext dc, Size size, double t, double? warpElapsed)
    {
        var canvasCenter = new Point(size.Width / 2, size.Height / 2);
        var parallax = Parallax;

        foreach (var star in _stars)
        {
            var depth = Math.Min(1.0, star.Radius / 2.2);
            var center = new Point(
                star.X * size.Width + parallax.X * depth,
                star.Y * size.Height + parallax.Y * depth);
            var twinkle = 0.72 + 0.28 * Math.Sin(t * star.TwinkleSpeed + star.Phase);
            var opacity = star.BaseOpacity * twinkle;
            var tint = StarTints[star.Tint];

            if (warpElapsed is >= 0)
            {
                var eased = Math.Pow(Math.Min(warpElapsed.Value / 0.35, 1), 2);
                var dx = center.X - canvasCenter.X;
                var dy = center.Y - canvasCenter.Y;
                var dist = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1);
                var dirX = dx / dist;
                var dirY = dy / dist;
                var streak = eased * (60 + 220 * depth);
                var end = new Point(center.X + dirX * streak, center.Y + dirY * streak);

                var penBrush = new LinearGradientBrush(
                    Color.FromArgb((byte)(opacity * 0.9 * 255), tint.R, tint.G, tint.B),
                    Color.FromArgb(0, tint.R, tint.G, tint.B),
                    new Point(0, 0), new Point(1, 0));
                // Approximate gradient along the streak
                penBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(end.X / size.Width, end.Y / size.Height),
                    EndPoint = new Point(center.X / size.Width, center.Y / size.Height),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb((byte)(opacity * 0.9 * 255), tint.R, tint.G, tint.B), 0),
                        new GradientStop(Color.FromArgb(0, tint.R, tint.G, tint.B), 1),
                    }
                };
                dc.DrawLine(new Pen(penBrush, star.Radius) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                    center, end);
                opacity = Math.Min(opacity * 1.25, 1);
            }

            var brush = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), tint.R, tint.G, tint.B));
            dc.DrawEllipse(brush, null, center, star.Radius, star.Radius);
        }
    }

    private void DrawPlanets(DrawingContext dc, Size size)
    {
        var drift = new Vector(Parallax.X * 0.15, Parallax.Y * 0.15);

        Planet(dc,
            new Point(size.Width * 0.12 + drift.X, size.Height * 0.18 + drift.Y), 44,
            Color.FromRgb(0xB8, 0x99, 0xF2), Color.FromRgb(0x33, 0x1F, 0x61));
        Planet(dc,
            new Point(size.Width * 0.86 + drift.X, size.Height * 0.14 + drift.Y), 22,
            Color.FromRgb(0x8C, 0xD9, 0xD9), Color.FromRgb(0x14, 0x40, 0x52));

        var ringed = new Point(size.Width * 0.85 + drift.X, size.Height * 0.72 + drift.Y);
        Planet(dc, ringed, 30,
            Color.FromRgb(0xF2, 0xBF, 0x80), Color.FromRgb(0x66, 0x38, 0x1A));
        PlanetRing(dc, ringed, 30, Color.FromArgb(128, 0xD9, 0xBF, 0x99), -0.32);
    }

    private static void Planet(DrawingContext dc, Point center, double radius, Color light, Color dark)
    {
        var glow = new RadialGradientBrush(
            Color.FromArgb(46, light.R, light.G, light.B),
            Color.FromArgb(0, light.R, light.G, light.B));
        dc.DrawEllipse(glow, null, center, radius * 1.7, radius * 1.7);

        var body = new RadialGradientBrush(light, dark)
        {
            GradientOrigin = new Point(0.3, 0.3),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.85,
            RadiusY = 0.85
        };
        dc.DrawEllipse(body, null, center, radius, radius);
    }

    private static void PlanetRing(DrawingContext dc, Point center, double radius, Color color, double tilt)
    {
        var geo = new EllipseGeometry(center, radius * 1.9, radius * 0.55);
        var rotated = new RotateTransform(tilt * 180 / Math.PI, center.X, center.Y);
        geo.Transform = rotated;
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), 2.5), geo);
    }

    private static void DrawMeteor(DrawingContext dc, Size size, double t)
    {
        const double slotLength = 6;
        var slot = (int)(t / slotLength);
        var jitter = UnitRandom(slot, 1) * (slotLength - 1.5);
        var local = t - slot * slotLength - jitter;
        const double duration = 0.9;
        if (local < 0 || local > duration) return;

        var progress = local / duration;
        var fade = Math.Sin(progress * Math.PI);

        var start = new Point(
            size.Width * (0.15 + 0.7 * UnitRandom(slot, 2)),
            size.Height * (0.05 + 0.35 * UnitRandom(slot, 3)));
        var dirSign = UnitRandom(slot, 4) > 0.5 ? 1.0 : -1.0;
        var angle = 0.45 + 0.35 * UnitRandom(slot, 5);
        var dir = new Vector(Math.Cos(angle) * dirSign, Math.Sin(angle));
        var travel = 380 + 180 * UnitRandom(slot, 6);

        var head = new Point(start.X + dir.X * travel * progress, start.Y + dir.Y * travel * progress);
        var tailLength = 110 * Math.Min(1, progress * 3 + 0.2);
        var tail = new Point(head.X - dir.X * tailLength, head.Y - dir.Y * tailLength);

        var white = Color.FromArgb((byte)(0.9 * fade * 255), 255, 255, 255);
        var blue = Color.FromArgb((byte)(0.35 * fade * 255), 0xBF, 0xD9, 0xFF);
        dc.DrawLine(new Pen(new SolidColorBrush(blue), 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, head, tail);
        dc.DrawLine(new Pen(new SolidColorBrush(white), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, head, tail);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(fade * 255), 255, 255, 255)), null, head, 2.2, 2.2);
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

    private readonly struct Star
    {
        public double X { get; init; }
        public double Y { get; init; }
        public double Radius { get; init; }
        public double BaseOpacity { get; init; }
        public double TwinkleSpeed { get; init; }
        public double Phase { get; init; }
        public int Tint { get; init; }

        public static Star[] MakeField(int count)
        {
            var rng = new Random(42);
            var tints = new[] { 0, 0, 0, 0, 0, 0, 1, 1, 1, 2 };
            var stars = new Star[count];
            for (int i = 0; i < count; i++)
            {
                stars[i] = new Star
                {
                    X = rng.NextDouble(),
                    Y = rng.NextDouble(),
                    Radius = (0.5 + rng.NextDouble() * 1.3) * (rng.NextDouble() > 0.93 ? 2.2 : 1),
                    BaseOpacity = 0.25 + rng.NextDouble() * 0.65,
                    TwinkleSpeed = 0.4 + rng.NextDouble() * 1.2,
                    Phase = rng.NextDouble() * Math.PI * 2,
                    Tint = tints[rng.Next(tints.Length)]
                };
            }
            return stars;
        }
    }
}
