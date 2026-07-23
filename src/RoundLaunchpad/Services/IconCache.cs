using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RoundLaunchpad.Services;

public static class IconCache
{
    private static readonly Dictionary<string, ImageSource> Icons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Color> Glows = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource GetIcon(string path, int size = 96)
    {
        var key = $"{path}#{size}";
        if (Icons.TryGetValue(key, out var cached)) return cached;

        var src = LoadIcon(path, size);
        Icons[key] = src;
        return src;
    }

    public static Color GlowColor(string path)
    {
        if (Glows.TryGetValue(path, out var c)) return c;
        c = ComputeGlow(path);
        Glows[path] = c;
        return c;
    }

    public static System.Windows.Media.Brush GlowBrush(string path)
    {
        var c = GlowColor(path);
        return new SolidColorBrush(c);
    }

    private static ImageSource LoadIcon(string path, int size)
    {
        try
        {
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var target = ShortcutResolver.Resolve(path);
                if (!string.IsNullOrEmpty(target) && File.Exists(target))
                    path = target!;
            }

            using var icon = Extract(path, size);
            if (icon != null)
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(size, size));
                src.Freeze();
                return src;
            }
        }
        catch
        {
            // fall through
        }

        return Fallback(size);
    }

    private static System.Drawing.Icon? Extract(string path, int size)
    {
        try
        {
            // Prefer shell large icon
            var sh = new SHFILEINFO();
            var flags = SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES;
            if (File.Exists(path) || Directory.Exists(path))
                flags = SHGFI_ICON | (size >= 48 ? SHGFI_LARGEICON : SHGFI_SMALLICON);

            var hr = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref sh, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (hr != IntPtr.Zero && sh.hIcon != IntPtr.Zero)
            {
                var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(sh.hIcon).Clone();
                DestroyIcon(sh.hIcon);
                return icon;
            }
        }
        catch { /* ignore */ }

        try
        {
            return System.Drawing.Icon.ExtractAssociatedIcon(path);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource Fallback(int size)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C)),
                null,
                new System.Windows.Point(size / 2.0, size / 2.0),
                size / 2.2,
                size / 2.2);
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    private static Color ComputeGlow(string path)
    {
        try
        {
            var src = GetIcon(path, 32) as BitmapSource;
            if (src == null) return Colors.White;

            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            var pixels = new byte[w * h * 4];
            conv.CopyPixels(pixels, w * 4, 0);

            const int buckets = 12;
            var weight = new double[buckets];
            var red = new double[buckets];
            var green = new double[buckets];
            var blue = new double[buckets];

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
                if (a < 128) continue;
                RgbToHsb(r / 255.0, g / 255.0, b / 255.0, out var hue, out var sat, out var bri);
                if (sat < 0.25 || bri < 0.2) continue;
                int idx = Math.Min(buckets - 1, (int)(hue * buckets));
                var ww = sat * bri;
                weight[idx] += ww;
                red[idx] += r / 255.0 * ww;
                green[idx] += g / 255.0 * ww;
                blue[idx] += b / 255.0 * ww;
            }

            int best = 0;
            for (int i = 1; i < buckets; i++)
                if (weight[i] > weight[best]) best = i;

            if (weight[best] > 6)
            {
                var rr = red[best] / weight[best];
                var gg = green[best] / weight[best];
                var bb = blue[best] / weight[best];
                RgbToHsb(rr, gg, bb, out var hue2, out var sat2, out var val2);
                sat2 = Math.Min(sat2 * 1.15, 1.0);
                val2 = Math.Max(val2, 0.8);
                HsbToRgb(hue2, sat2, val2, out rr, out gg, out bb);
                return Color.FromRgb((byte)(rr * 255), (byte)(gg * 255), (byte)(bb * 255));
            }
        }
        catch
        {
            // ignore
        }
        return Colors.White;
    }

    private static void RgbToHsb(double r, double g, double b, out double h, out double s, out double v)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        v = max;
        var delta = max - min;
        s = max <= 0 ? 0 : delta / max;
        if (delta <= 1e-9) { h = 0; return; }
        if (Math.Abs(max - r) < 1e-9) h = (g - b) / delta + (g < b ? 6 : 0);
        else if (Math.Abs(max - g) < 1e-9) h = (b - r) / delta + 2;
        else h = (r - g) / delta + 4;
        h /= 6.0;
    }

    private static void HsbToRgb(double h, double s, double v, out double r, out double g, out double b)
    {
        if (s <= 0) { r = g = b = v; return; }
        h = (h - Math.Floor(h)) * 6.0;
        var i = (int)Math.Floor(h);
        var f = h - i;
        var p = v * (1 - s);
        var q = v * (1 - s * f);
        var t = v * (1 - s * (1 - f));
        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
