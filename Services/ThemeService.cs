using System.Windows;
using System.Windows.Media;

namespace FDLike2026.Services;

/// <summary>
/// Theme の内容を Application.Resources に流し込む。
/// XAML 側は DynamicResource で参照しているので、実行中に呼べば即座に画面へ反映される。
/// </summary>
public static class ThemeService
{
    private const string Fallback = "BIZ UDゴシック, MS Gothic, ＭＳ ゴシック, Consolas";

    /// <summary>アイコン表示の ON/OFF を画面へ反映（列幅と可視性）。</summary>
    public static void ApplyIcons(bool show)
    {
        var res = Application.Current.Resources;
        res["IconVisibility"] = show ? Visibility.Visible : Visibility.Collapsed;
        res["IconOffVisibility"] = show ? Visibility.Collapsed : Visibility.Visible;
        res["ListColIcon"] = new GridLength(show ? 20 : 0);
    }

    public static void Apply(Theme theme)
    {
        var res = Application.Current.Resources;

        foreach (var it in Theme.Items)
        {
            var hex = theme.Colors.TryGetValue(it.Key, out var h) ? h : "#FFFFFF";
            res[it.Key + "Brush"] = Brush(hex);
            res[it.Key + "Color"] = ParseColor(hex);   // SystemColors の差し替え（ツリー・リストの選択色）用
        }

        // ---- 旧キー名の別名（既存 XAML との互換） ----
        res["BgBrush"]        = res["OtherBgBrush"];
        res["TextBrush"]      = res["OtherFgBrush"];
        res["LabelBrush"]     = res["InfoItemBrush"];
        res["HeadingBrush"]   = res["InfoTitleBrush"];
        res["NumberBrush"]    = res["FileBrush"];
        res["DimBrush"]       = res["OtherDimBrush"];
        res["ClockBrush"]     = res["InfoTimeBrush"];
        res["KeyLetterBrush"] = res["FuncKeyBgBrush"];
        res["KeyLabelBrush"]  = res["FuncLabelBrush"];

        // ---- フォント（領域別） ----
        SetFont(res, "Info",  theme.InfoFont);
        SetFont(res, "List",  theme.ListFont);
        SetFont(res, "Tree",  theme.TreeFont);
        SetFont(res, "Other", theme.OtherFont);
        // 旧キー名
        res["MonoFont"] = res["OtherFontFamily"];
        res["BaseFontSize"] = res["OtherFontSize"];
        res["HeaderFontSize"] = theme.InfoFont.Size + 2;

        // ---- 列幅: フォントの文字幅から計算（フォントを大きくしても重ならない） ----
        ListModeService.Apply(ListModeService.Current, ListModeService.DirLabel, theme);   // 一覧の列幅は表示モードと合わせて計算
        double ic = CharWidth(theme.InfoFont);
        res["InfoColNum"]   = new GridLength(Math.Ceiling(ic * 16));   // "15,746,400,256"
        res["InfoColCount"] = new GridLength(Math.Ceiling(ic * 7));    // "9999 個"
        res["InfoColAttr"]  = new GridLength(Math.Ceiling(ic * 9));    // "RHSAD" / "*.*"

        // ---- 文字の描画 ----
        switch (theme.TextRendering)
        {
            case "aliased":
                res["TextRenderingMode"] = TextRenderingMode.Aliased;
                res["TextFormattingMode"] = TextFormattingMode.Display;
                break;
            case "grayscale":
                res["TextRenderingMode"] = TextRenderingMode.Grayscale;
                res["TextFormattingMode"] = TextFormattingMode.Ideal;
                break;
            default:
                res["TextRenderingMode"] = TextRenderingMode.ClearType;
                res["TextFormattingMode"] = TextFormattingMode.Ideal;
                break;
        }

        // ---- 行間ギャップ / タグマーク ----
        res["ListRowPadding"] = new Thickness(2, theme.LineGap / 2.0, 2, theme.LineGap - theme.LineGap / 2.0);
        // ビューア: その他フォントの自然な行高 + 行間
        try
        {
            var vf = BuildFontFamily(theme.OtherFont.Family);
            res["ViewerLineHeight"] = Math.Ceiling(theme.OtherFont.Size * vf.LineSpacing) + theme.ViewerLineGap;
        }
        catch { res["ViewerLineHeight"] = theme.OtherFont.Size * 1.3 + theme.ViewerLineGap; }
        res["TreeRowPadding"] = new Thickness(2, theme.TreeLineGap / 2.0, 2, theme.TreeLineGap - theme.TreeLineGap / 2.0);
        res["TagMarkText"] = theme.TagMark == "star" ? "*" : "▶";
    }

    /// <summary>等幅フォント前提で、半角 1 文字分の幅（px）を実測する。</summary>
    public static double CharWidth(FontSpec f)
    {
        try
        {
            var tf = new Typeface(BuildFontFamily(f.Family), FontStyles.Normal, f.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            var ft = new FormattedText("0000000000", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                tf, f.Size, Brushes.White, 1.0);
            return Math.Max(4, ft.WidthIncludingTrailingWhitespace / 10);
        }
        catch { return f.Size * 0.55; }
    }

    private static void SetFont(ResourceDictionary res, string prefix, FontSpec f)
    {
        res[prefix + "FontFamily"] = BuildFontFamily(f.Family);
        res[prefix + "FontSize"] = f.Size;
        res[prefix + "FontWeight"] = f.Bold ? FontWeights.Bold : FontWeights.Normal;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush(ParseColor(hex));
        b.Freeze();
        return b;
    }

    /// <summary>指定フォントが無い環境でも崩れないよう、等幅のフォールバックを後ろに付ける。</summary>
    public static FontFamily BuildFontFamily(string name)
    {
        var list = string.IsNullOrWhiteSpace(name) ? Fallback : $"{name}, {Fallback}";
        try { return new FontFamily(list); }
        catch { return new FontFamily(Fallback); }
    }

    public static Color ParseColor(string hex)
    {
        try
        {
            var obj = ColorConverter.ConvertFromString(hex.Trim());
            if (obj is Color c) return c;
        }
        catch { }
        return Colors.White;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>インストール済みフォントのうち、日本語環境でよく使う等幅系を先頭に出した一覧。</summary>
    public static IReadOnlyList<string> InstalledFontNames()
    {
        var ja = System.Windows.Markup.XmlLanguage.GetLanguage("ja-jp");
        var names = Fonts.SystemFontFamilies
            .Select(f => f.FamilyNames.TryGetValue(ja, out var n) ? n : f.Source)
            .Distinct()
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var preferred = new[] { "BIZ UDゴシック", "MS Gothic", "ＭＳ ゴシック", "UD デジタル 教科書体 N", "Consolas", "Cascadia Mono", "Cascadia Code", "HackGen", "PlemolJP", "Source Han Code JP", "Myrica M", "Ricty Diminished" };
        var head = preferred.Where(names.Contains).ToList();
        return head.Concat(names.Except(head)).ToList();
    }
}
