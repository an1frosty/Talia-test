using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinSetup.UI;

/// <summary>Presentation-only classification of the human-readable status strings produced by the install engine.</summary>
public enum StatusKind { Neutral, Waiting, Working, Success, Already, Warning, Error, Cancelled, Setup }

public static class StatusClassifier
{
    public static StatusKind Classify(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return StatusKind.Neutral;
        var t = status.Trim();
        bool Starts(string prefix) => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        bool Has(string part) => t.Contains(part, StringComparison.OrdinalIgnoreCase);

        if (Starts("Ошибка")) return StatusKind.Error;
        if (Starts("Не удалось")) return StatusKind.Warning;
        if (Starts("Отменено")) return StatusKind.Cancelled;
        if (Starts("В очереди")) return StatusKind.Waiting;
        if (Starts("Установка...") || Starts("Подготовка...") || Starts("Проверка установки...")) return StatusKind.Working;
        if (Starts("Найдена в WinGet")) return StatusKind.Warning;
        if (Starts("Уже установлена")) return StatusKind.Already;
        if (Starts("Требует")) return StatusKind.Setup;
        if (Starts("Установлена") || Starts("Установка завершена"))
            return Has("предупреждение") || Has("перезагрузка") || Has("другой путь") || Has("путь не определён") || Has("путь не подтверждён")
                ? StatusKind.Warning
                : StatusKind.Success;
        return StatusKind.Neutral;
    }
}

/// <summary>Shared frozen brushes for converters (same hex values as Themes/Talia.xaml).</summary>
internal static class TaliaBrushes
{
    private static SolidColorBrush Make(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public static readonly SolidColorBrush Pink = Make("#E99BC5");
    public static readonly SolidColorBrush Silver = Make("#C8CDD8");
    public static readonly SolidColorBrush Red = Make("#A83250");
    public static readonly SolidColorBrush RedText = Make("#E57A93");
    public static readonly SolidColorBrush Icy = Make("#9BAEDC");
    public static readonly SolidColorBrush Skin = Make("#D9A99B");
    public static readonly SolidColorBrush Muted = Make("#77737E");
    public static readonly SolidColorBrush Secondary = Make("#A5A1AA");

    public static readonly Color[] MonogramColors =
    [
        (Color)ColorConverter.ConvertFromString("#E99BC5"),
        (Color)ColorConverter.ConvertFromString("#9BAEDC"),
        (Color)ColorConverter.ConvertFromString("#D9A99B"),
        (Color)ColorConverter.ConvertFromString("#C8CDD8")
    ];
}

/// <summary>string status -> StatusKind name (used by DataTriggers).</summary>
public sealed class StatusKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => StatusClassifier.Classify(value as string).ToString();
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>string status -> brush. ConverterParameter "text" returns a text-safe tint; default is the dot colour.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = string.Equals(parameter as string, "text", StringComparison.OrdinalIgnoreCase);
        return StatusClassifier.Classify(value as string) switch
        {
            StatusKind.Working => TaliaBrushes.Pink,
            StatusKind.Success => TaliaBrushes.Pink,
            StatusKind.Waiting => TaliaBrushes.Silver,
            StatusKind.Already => TaliaBrushes.Icy,
            StatusKind.Setup => TaliaBrushes.Icy,
            StatusKind.Warning => TaliaBrushes.Skin,
            StatusKind.Error => text ? TaliaBrushes.RedText : TaliaBrushes.Red,
            StatusKind.Cancelled => text ? TaliaBrushes.Secondary : TaliaBrushes.Silver,
            _ => text ? TaliaBrushes.Secondary : TaliaBrushes.Muted
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Application name -> single-letter monogram for the card icon.</summary>
public sealed class NameInitialConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = (value as string)?.Trim() ?? "";
        return name.Length == 0 ? "?" : name[..1].ToUpperInvariant();
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Application name -> deterministic tint. ConverterParameter "bg" returns a translucent background, otherwise the glyph colour.</summary>
public sealed class NameTintConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string ?? "";
        var sum = 0;
        foreach (var ch in name) sum += ch;
        var color = TaliaBrushes.MonogramColors[sum % TaliaBrushes.MonogramColors.Length];
        if (string.Equals(parameter as string, "bg", StringComparison.OrdinalIgnoreCase))
            color = Color.FromArgb(0x26, color.R, color.G, color.B);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Empty / whitespace string -> Collapsed, otherwise Visible.</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Applies the dark Windows title bar (Windows 10 2004+/11). Silently does nothing where unsupported.</summary>
internal static class TitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;
                var dark = 1;
                DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));          // DWMWA_USE_IMMERSIVE_DARK_MODE
                var caption = 0x00100B0B;                                           // Obsidian #0B0B10 (COLORREF 0x00BBGGRR)
                DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));        // DWMWA_CAPTION_COLOR (Windows 11)
                var text = 0x00F3EFF2;                                              // #F2EFF3
                DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));           // DWMWA_TEXT_COLOR (Windows 11)
            }
            catch { /* cosmetic only */ }
        };
    }

    /// <summary>Keeps a window inside the primary work area (DIPs) so it never opens off-screen or taller than the screen.</summary>
    public static void FitToWorkArea(Window window)
    {
        var area = SystemParameters.WorkArea;
        window.MaxHeight = Math.Min(window.MaxHeight, Math.Max(320, area.Height - 16));
        window.MaxWidth = Math.Min(window.MaxWidth, Math.Max(480, area.Width - 16));
        if (window.MinWidth > area.Width) window.MinWidth = area.Width - 16;
        if (window.MinHeight > area.Height) window.MinHeight = area.Height - 16;
        if (window.Width > window.MaxWidth) window.Width = window.MaxWidth;
        if (window.Height > window.MaxHeight) window.Height = window.MaxHeight;
    }
}
