using Avalonia.Media;
using Sonoric.Models;

namespace Sonoric.ViewModels;

public sealed class MoodVisual
{
    public MoodVisual(TagRecord record)
    {
        Id = record.Id;
        Name = string.IsNullOrWhiteSpace(record.Name) ? "Без названия" : record.Name;
        if (!Color.TryParse(record.Hex, out var color))
        {
            color = Colors.White;
        }

        Hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        Color = color;
        Accent = new SolidColorBrush(color);
        var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
        Foreground = luminance > 0.55 ? Brushes.Black : Brushes.White;
        IdleBackground = new SolidColorBrush(Color.FromRgb(17, 17, 17));
    }

    public string Id { get; }
    public string Name { get; }
    public string Hex { get; }
    public Color Color { get; }
    public IBrush Accent { get; }
    public IBrush Foreground { get; }
    public IBrush IdleBackground { get; }
}

public sealed class MoodDot
{
    public MoodDot(IBrush accent, IEnumerable<string> names)
    {
        Accent = accent;
        Tooltip = string.Join(", ", names);
    }

    public IBrush Accent { get; }
    public string Tooltip { get; }
}
