using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Sonoric.Models;

namespace Sonoric.Services;

public static class CoverAccentService
{
    public static IBrush DefaultBackdrop { get; } = new SolidColorBrush(Colors.Black);

    public static Color Resolve(string? coverPath, AlbumAccentMode mode, string? customHex)
    {
        if (mode == AlbumAccentMode.Custom && TryParseHex(customHex, out var custom))
        {
            return custom;
        }

        if (string.IsNullOrWhiteSpace(coverPath) || !File.Exists(coverPath))
        {
            return Colors.Black;
        }

        try
        {
            var samples = Sample(coverPath);
            if (samples.Count == 0)
            {
                return Colors.Black;
            }

            var color = mode switch
            {
                AlbumAccentMode.Average => Average(samples),
                AlbumAccentMode.Contrast => Contrast(samples),
                _ => Dominant(samples)
            };
            return Tune(color);
        }
        catch
        {
            return Colors.Black;
        }
    }

    public static IBrush CreateBackdrop(Color accent)
    {
        if (accent.R == 0 && accent.G == 0 && accent.B == 0)
        {
            return DefaultBackdrop;
        }

        var mid = Color.FromRgb(
            (byte)(accent.R * 0.42),
            (byte)(accent.G * 0.42),
            (byte)(accent.B * 0.42));
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(accent, 0),
                new GradientStop(accent, 0.18),
                new GradientStop(mid, 0.52),
                new GradientStop(Colors.Black, 1)
            }
        };
    }

    private static List<(byte R, byte G, byte B)> Sample(string path)
    {
        var samples = new List<(byte, byte, byte)>();
        using var bitmap = new Bitmap(path);
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        if (width <= 0 || height <= 0)
        {
            return samples;
        }

        var scale = Math.Min(1.0, 72.0 / Math.Max(width, height));
        var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(height * scale));
        using var scaled = bitmap.CreateScaledBitmap(new PixelSize(targetWidth, targetHeight), BitmapInterpolationMode.LowQuality);
        var stride = targetWidth * 4;
        var size = stride * targetHeight;
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            scaled.CopyPixels(new PixelRect(0, 0, targetWidth, targetHeight), pointer, size, stride);
            var data = new byte[size];
            Marshal.Copy(pointer, data, 0, size);
            var fallback = new List<(byte, byte, byte)>();
            for (var i = 0; i + 3 < data.Length; i += 4)
            {
                var b = data[i];
                var g = data[i + 1];
                var r = data[i + 2];
                var a = data[i + 3];
                if (a < 40)
                {
                    continue;
                }

                fallback.Add((r, g, b));
                if (IsUsable(r, g, b))
                {
                    samples.Add((r, g, b));
                }
            }

            return samples.Count > 0 ? samples : fallback;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static bool IsUsable(byte r, byte g, byte b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        if (max < 22)
        {
            return false;
        }

        if (min > 242 && max - min < 18)
        {
            return false;
        }

        return true;
    }

    private static Color Average(List<(byte R, byte G, byte B)> samples)
    {
        long r = 0;
        long g = 0;
        long b = 0;
        foreach (var sample in samples)
        {
            r += sample.R;
            g += sample.G;
            b += sample.B;
        }

        var count = samples.Count;
        return Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    private static Color Dominant(List<(byte R, byte G, byte B)> samples)
    {
        return Quantize(samples)
            .OrderByDescending(item => item.Count)
            .Select(item => item.Color)
            .First();
    }

    private static Color Contrast(List<(byte R, byte G, byte B)> samples)
    {
        var average = Average(samples);
        var buckets = Quantize(samples);
        var minCount = Math.Max(4, samples.Count / 200);
        var candidates = buckets.Where(item => item.Count >= minCount).ToList();
        if (candidates.Count == 0)
        {
            candidates = buckets;
        }

        return candidates
            .OrderByDescending(item => Distance(item.Color, average) * (0.35 + Saturation(item.Color)) / Math.Sqrt(item.Count))
            .Select(item => item.Color)
            .First();
    }

    private static List<(Color Color, int Count)> Quantize(List<(byte R, byte G, byte B)> samples)
    {
        var buckets = new Dictionary<int, (int R, int G, int B, int Count)>();
        foreach (var sample in samples)
        {
            var key = (sample.R >> 4 << 8) | (sample.G >> 4 << 4) | (sample.B >> 4);
            if (buckets.TryGetValue(key, out var bucket))
            {
                buckets[key] = (bucket.R + sample.R, bucket.G + sample.G, bucket.B + sample.B, bucket.Count + 1);
            }
            else
            {
                buckets[key] = (sample.R, sample.G, sample.B, 1);
            }
        }

        return buckets.Values
            .Select(item => (Color.FromRgb(
                (byte)(item.R / item.Count),
                (byte)(item.G / item.Count),
                (byte)(item.B / item.Count)), item.Count))
            .ToList();
    }

    private static Color Tune(Color color)
    {
        RgbToHsl(color.R, color.G, color.B, out var h, out var s, out var l);
        if (s < 0.08)
        {
            return Colors.Black;
        }

        s = Math.Clamp(s * 1.12, 0.38, 0.86);
        l = Math.Clamp(l, 0.28, 0.46);
        HslToRgb(h, s, l, out var r, out var g, out var b);
        return Color.FromRgb(r, g, b);
    }

    private static double Distance(Color a, Color b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static double Saturation(Color color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B)) / 255.0;
        var min = Math.Min(color.R, Math.Min(color.G, color.B)) / 255.0;
        return max <= 0 ? 0 : (max - min) / max;
    }

    private static bool TryParseHex(string? hex, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var value = hex.Trim();
        if (!value.StartsWith('#'))
        {
            value = "#" + value;
        }

        return Color.TryParse(value, out color);
    }

    private static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        var rd = r / 255.0;
        var gd = g / 255.0;
        var bd = b / 255.0;
        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        l = (max + min) / 2.0;
        if (Math.Abs(max - min) < 0.0001)
        {
            h = 0;
            s = 0;
            return;
        }

        var d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        if (max == rd)
        {
            h = ((gd - bd) / d + (gd < bd ? 6 : 0)) / 6.0;
        }
        else if (max == gd)
        {
            h = ((bd - rd) / d + 2) / 6.0;
        }
        else
        {
            h = ((rd - gd) / d + 4) / 6.0;
        }
    }

    private static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
    {
        double Hue(double p, double q, double t)
        {
            if (t < 0)
            {
                t += 1;
            }

            if (t > 1)
            {
                t -= 1;
            }

            if (t < 1.0 / 6.0)
            {
                return p + (q - p) * 6 * t;
            }

            if (t < 0.5)
            {
                return q;
            }

            if (t < 2.0 / 3.0)
            {
                return p + (q - p) * (2.0 / 3.0 - t) * 6;
            }

            return p;
        }

        double rd;
        double gd;
        double bd;
        if (s <= 0)
        {
            rd = l;
            gd = l;
            bd = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            rd = Hue(p, q, h + 1.0 / 3.0);
            gd = Hue(p, q, h);
            bd = Hue(p, q, h - 1.0 / 3.0);
        }

        r = (byte)Math.Round(rd * 255);
        g = (byte)Math.Round(gd * 255);
        b = (byte)Math.Round(bd * 255);
    }
}
