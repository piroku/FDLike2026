using System.Windows;

namespace FDLike2026.Services;

/// <summary>
/// WinFM2008 の「カスタマイズ › リスト」の表示モード。5 つ持ち、Shift+1～5 で切り替える。
/// </summary>
public sealed class ListMode
{
    public string Name { get; set; } = "";

    // ---- ファイル名 ----
    /// <summary>"auto"（文字数自動）/ "fixed"（文字数指定）/ "dos"（8.3 形式）</summary>
    public string FileNameMode { get; set; } = "auto";
    public int FileNameChars { get; set; } = 12;

    // ---- 拡張子 ----
    public bool SplitExt { get; set; } = true;
    public int ExtChars { get; set; } = 6;
    public bool DirNoSplit { get; set; } = true;

    // ---- 日付と時刻 ----
    /// <summary>"write"（更新）/ "create"（作成）/ "access"（アクセス）</summary>
    public string DateKind { get; set; } = "write";
    public bool ShowDate { get; set; } = true;
    public bool Year4 { get; set; } = false;
    public bool ShowTime { get; set; } = true;
    public bool ShowSeconds { get; set; } = false;
    public int DatePad { get; set; } = 1;

    // ---- サイズ / 属性 ----
    public bool ShowOverflow { get; set; } = true;
    public bool ShowSize { get; set; } = true;
    public bool SizeShort { get; set; } = true;     // 簡略表示（MB, KB など）
    public bool ShowAttr { get; set; } = true;
    public bool AttrFilmtn { get; set; } = true;

    // ---- FDLike 独自: 右ウィンドウ ----
    public bool ShowRightPane { get; set; } = true;
    // ---- FDLike 独自: git の状態列（M / U / A / D / ! とフォルダの ●） ----
    public bool ShowGit { get; set; } = true;

    public ListMode Clone() => (ListMode)MemberwiseClone();

    public static List<ListMode> Defaults() => new()
    {
        new() { Name = "モード:1", ShowRightPane = true },
        new() { Name = "モード:2", ShowRightPane = false },
        new() { Name = "モード:3", ShowRightPane = true, ShowAttr = false, ShowSeconds = false },
        new() { Name = "モード:4", ShowRightPane = true, ShowDate = false, ShowTime = false, ShowAttr = false },
        new() { Name = "モード:5", ShowRightPane = false, Year4 = true, ShowSeconds = true, SizeShort = false },
    };
}

/// <summary>いま有効な表示モードを FileEntry と画面に反映する。</summary>
public static class ListModeService
{
    public static ListMode Current { get; private set; } = new();
    public static string DirLabel { get; private set; } = "< DIR >";
    /// <summary>いま表示中のモード番号（0～4）。MainWindow が設定する。</summary>
    public static int CurrentIndex { get; set; }
    /// <summary>設定画面などで Apply された後に発火。MainWindow が一覧の再計算と右ウィンドウの表示に使う。</summary>
    public static event Action? Changed;
    public static void RaiseChanged() => Changed?.Invoke();

    public static void Apply(ListMode mode, string dirLabel, Theme theme)
    {
        Current = mode;
        DirLabel = string.IsNullOrWhiteSpace(dirLabel) ? "< DIR >" : dirLabel;
        var res = Application.Current.Resources;
        double lc = ThemeService.CharWidth(theme.ListFont);
        GridLength Col(double chars) => new(Math.Ceiling(lc * chars));

        res["ListColTag"]  = Col(1.5);
        res["ListColName"] = mode.FileNameMode == "fixed" ? Col(mode.FileNameChars + 1) : new GridLength(1, GridUnitType.Star); // 自動時は各ペインの NameColWidth が使われる
        res["ListColExt"]  = mode.SplitExt ? Col(mode.ExtChars + 1) : new GridLength(0);
        res["ListColSize"] = mode.ShowSize ? Col(mode.SizeShort ? 10 : 15) : new GridLength(0);
        res["ListColDate"] = mode.ShowDate ? Col(mode.DatePad + (mode.Year4 ? 10.5 : 8.5)) : new GridLength(0);
        res["ListColTime"] = mode.ShowTime ? Col(mode.ShowSeconds ? 9 : 6) : new GridLength(0);
        res["ListColAttr"] = mode.ShowAttr ? Col(6) : new GridLength(0);
        res["ListColGit"]  = mode.ShowGit ? Col(4) : new GridLength(0);   // ● は全角幅なので余裕を持たせる
        // 名前列は内容に合わせた幅にし、残りを右端の余白へ（WinFM と同じ左詰め）
        res["ListColFill"] = new GridLength(1, GridUnitType.Star);
        _theme = theme;
    }

    private static Theme? _theme;

    /// <summary>
    /// 名前列の幅を決める。文字数自動: 一覧内で最も長い名前の実測幅（最低 8 文字分、上限 80 文字分）。
    /// 文字数指定 / 8.3: 設定どおりの固定幅。
    /// </summary>
    /// <summary>
    /// 拡張子列の幅。文字数自動のときは一覧内でいちばん長い拡張子に合わせる（最低は設定の文字数）。
    /// それ以外は設定の文字数で固定。分離しないなら 0。
    /// </summary>
    public static GridLength MeasureExtColumn(IEnumerable<string> exts)
    {
        var theme = _theme ?? new Theme();
        double lc = ThemeService.CharWidth(theme.ListFont);
        if (!Current.SplitExt) return new GridLength(0);
        double fixedW = Math.Ceiling(lc * (Current.ExtChars + 1));
        if (Current.FileNameMode != "auto") return new GridLength(fixedW);
        double max = 0;
        try
        {
            var f = theme.ListFont;
            var tf = new System.Windows.Media.Typeface(ThemeService.BuildFontFamily(f.Family), FontStyles.Normal,
                f.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            foreach (var x in exts)
            {
                if (string.IsNullOrEmpty(x)) continue;
                var ft = new System.Windows.Media.FormattedText(x, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    tf, f.Size, System.Windows.Media.Brushes.White, 1.0);
                if (ft.WidthIncludingTrailingWhitespace > max) max = ft.WidthIncludingTrailingWhitespace;
            }
        }
        catch { max = exts.Select(x => x?.Length ?? 0).DefaultIfEmpty(0).Max() * lc; }
        return new GridLength(Math.Ceiling(Math.Clamp(Math.Max(fixedW, max + lc), lc * 3, lc * 30)));
    }

    /// <summary>名前列以外（タグ・アイコン・サイズ・日付・時刻・属性・git）の固定幅の合計。</summary>
    public static double FixedColumnsWidth()
    {
        var res = Application.Current?.Resources;
        if (res is null) return 0;
        double sum = 0;
        foreach (var key in new[] { "ListColTag", "ListColIcon", "ListColSize", "ListColDate", "ListColTime", "ListColAttr", "ListColGit" })
            if (res[key] is GridLength g && g.IsAbsolute) sum += g.Value;
        return sum + 16;   // 余白
    }

    public static GridLength MeasureNameColumn(IEnumerable<string> names, double maxWidth = 0)
    {
        var theme = _theme ?? new Theme();
        double lc = ThemeService.CharWidth(theme.ListFont);
        if (Current.FileNameMode == "fixed") return new GridLength(Math.Ceiling(lc * (Current.FileNameChars + 1)));
        if (Current.FileNameMode == "dos") return new GridLength(Math.Ceiling(lc * 13));

        double max = 0;
        try
        {
            var f = theme.ListFont;
            var tf = new System.Windows.Media.Typeface(ThemeService.BuildFontFamily(f.Family), FontStyles.Normal,
                f.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                var ft = new System.Windows.Media.FormattedText(n, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    tf, f.Size, System.Windows.Media.Brushes.White, 1.0);
                if (ft.WidthIncludingTrailingWhitespace > max) max = ft.WidthIncludingTrailingWhitespace;
            }
        }
        catch { max = names.Select(n => n?.Length ?? 0).DefaultIfEmpty(0).Max() * lc; }
        double w = Math.Clamp(max + lc, lc * 8, lc * 80);
        // ウィンドウ幅に収まらないときは名前列を縮める（他の列が画面外に出ないように）
        if (maxWidth > 0) w = Math.Max(lc * 8, Math.Min(w, maxWidth));
        return new GridLength(Math.Ceiling(w));
    }
}
