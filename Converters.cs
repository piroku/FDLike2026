using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FDLike2026;

/// <summary>バイト数を "117,597,332 K" 形式に。</summary>
public sealed class KiloConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is long b ? $"{b / 1024:#,0} K" : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>整数をカンマ区切りに。</summary>
public sealed class CommaConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch { long l => l.ToString("#,0"), int i => i.ToString("#,0"), _ => "" };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>タグ付きなら "*"。</summary>
public sealed class TagMarkConverter : IValueConverter
{
    public static readonly TagMarkConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? "*" : " ";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>ツリー: ドライブは "■"、フォルダは "▸"。</summary>
public sealed class DriveIconConverter : IValueConverter
{
    public static readonly DriveIconConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? "■" : "▸";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>ソート方式の表示名。</summary>
public sealed class SortNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ViewModels.SortMode m ? m switch
        {
            ViewModels.SortMode.Name => "名前",
            ViewModels.SortMode.Extension => "拡張子",
            ViewModels.SortMode.Size => "サイズ",
            ViewModels.SortMode.Date => "日付",
            _ => "なし",
        } : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
