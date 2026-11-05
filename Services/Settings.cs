using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FDLike2026.Services;

/// <summary>%APPDATA%\FDLike2026\settings.json に保存する設定。</summary>
public sealed class Settings
{
    public string EditorPath { get; set; } = "notepad.exe";
    /// <summary>
    /// エディタで行を指定して開くときの引数書式。$F = ファイルのフルパス、$L = 行番号。
    /// 例: VS Code → -g "$F:$L"　サクラ → -Y=$L "$F"　秀丸 → /j$L "$F"　メモ帳など非対応 → 空（行指定なしで開く）
    /// </summary>
    public string EditorLineArgs { get; set; } = "";
    /// <summary>Shift+W: 比較ツール（WinMerge など）。空欄なら PATH と標準のインストール先から WinMergeU.exe を探す。</summary>
    public string DiffTool { get; set; } = "";
    /// <summary>Shift+F 文字列の検索: 対象ファイルの既定マスク。</summary>
    public string GrepFileMask { get; set; } = "*.txt *.md *.log *.csv *.ini *.json *.xml *.yaml *.yml *.cs *.vb *.bas *.frm *.cls *.c *.h *.cpp *.js *.ts *.py *.sql *.ps1 *.bat *.cmd *.html *.htm *.xaml *.csproj *.config";
    /// <summary>文字列の検索の履歴（新しい順、最大 30）。</summary>
    public List<string> GrepHistory { get; set; } = new();
    public bool GrepRegex { get; set; } = false;
    public bool GrepCaseSensitive { get; set; } = false;
    public bool GrepWholeWord { get; set; } = false;
    public int GrepMaxFileMB { get; set; } = 20;
    /// <summary>grep: Office 文書 / PDF の中身も探す（マスクに関係なく docx/xlsx/pptx/pdf/doc/xls/ppt… を対象に含める）。</summary>
    public bool GrepDocuments { get; set; } = true;
    /// <summary>ビューアの折り返し（W で切替、記憶する）。</summary>
    public bool ViewerWrap { get; set; } = true;
    public string LeftPath { get; set; } = "";
    public string RightPath { get; set; } = "";
    public bool ShowTree { get; set; } = true;
    public bool ShowHidden { get; set; } = true;
    public bool DeleteToRecycleBin { get; set; } = true;

    /// <summary>カスタマイズ › リスト: 5 つの表示モード（Shift+1～5）。</summary>
    public List<ListMode> ListModes { get; set; } = ListMode.Defaults();
    /// <summary>起動時のモード（0～4）。</summary>
    public int StartupListMode { get; set; } = 0;
    /// <summary>フォルダの表記（"< DIR >" / "[DIR]" / "<DIR>" / 空）。</summary>
    public string DirLabel { get; set; } = "< DIR >";

    /// <summary>Ctrl+I: ファイル一覧・ツリーにシェルアイコンを表示する。</summary>
    public bool ShowIcons { get; set; } = true;

    /// <summary>隠し属性のフォルダ/ファイルの並べ方: "mixed" / "first"（WinFM と同じ、既定）/ "last"</summary>
    public string HiddenGrouping { get; set; } = "first";

    /// <summary>J: JumpFolder の登録先。キーは "F1".."F12" / "A".."Z"。</summary>
    public Dictionary<string, string> JumpFolders { get; set; } = new();

    /// <summary>F2: コマンドマクロ。キーは "A".."Z" / "1".."9"、値は「タイトル,コマンド」。</summary>
    public Dictionary<string, string> Macros { get; set; } = new()
    {
        // 既定のマクロ（F2 → キー）。「タイトル,コマンド」。削除すれば消えたままになる
        ["V"] = "VS Code でカレントフォルダを開く,code \"$P\"",
        ["F"] = "VS Code でカーソルのファイルを開く,code \"$F\"",
        ["W"] = "WinMerge でフォルダ比較（左右）,WinMergeU.exe /r $Q$P$Q $Q$O$Q",
        ["M"] = "WinMerge でファイル比較（反対側の同名）,WinMergeU.exe $Q$P\\$C$Q $Q$O\\$C$Q",
    };


    /// <summary>複写・移動先の履歴（新しい順）。</summary>
    public List<string> CopyHistory { get; set; } = new();

    /// <summary>X: コマンドの実行の履歴。</summary>
    public List<string> ExecAppHistory { get; set; } = new();
    public List<string> ExecArgHistory { get; set; } = new();
    public bool ExecDos { get; set; } = true;
    public bool ExecNoPause { get; set; } = false;

    /// <summary>Ctrl+M: ミラーコピーの登録手順。</summary>
    public Dictionary<string, Views.MirrorProc> MirrorProcs { get; set; } = new();

    /// <summary>F4: 拡張子の対応。「拡張子,コマンド」の行。上から順に最初に一致したものを使う。</summary>
    public List<string> Associations { get; set; } = new();
    /// <summary>ツリーの幅（境界をマウスでドラッグして変更）。</summary>
    public double TreeWidth { get; set; } = 260;
    /// <summary>左一覧 : 右一覧 の幅の比（左 / 右）。</summary>
    public double PaneRatio { get; set; } = 1.0;
    public double WindowWidth { get; set; } = 1440;
    public double WindowHeight { get; set; } = 860;

    /// <summary>現在の配色・フォント（WinFM の「カスタマイズ › デザイン」相当）。</summary>
    public Theme Theme { get; set; } = Theme.Classic();

    /// <summary>名前付き配色（WinFM の「配色(S)」コンボ）。ユーザー保存分。組み込みは <see cref="Theme.BuiltIn"/>。</summary>
    public Dictionary<string, Theme> Schemes { get; set; } = new();
    public string CurrentScheme { get; set; } = "Classic";

    /// <summary>既定配色やキー体系を差し替えたときに古い保存値をリセットするための版数。</summary>
    public int ThemeVersion { get; set; } = CurrentThemeVersion;
    public const int CurrentThemeVersion = 9;

    // ---- 旧版互換 ----
    public double? FontSize { get; set; }

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FDLike2026", "settings.json");

    private static readonly JsonSerializerOptions JsonOpt = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOpt) ?? new Settings();
                if (s.ThemeVersion < 4)
                {
                    // 配色キーの体系が変わったので、配色は新しい既定（Classic）に戻す
                    s.Theme = Theme.Classic();
                    s.CurrentScheme = "Classic";
                    s.FontSize = null;
                }
                if (s.ThemeVersion < 5)
                {
                    // v5: 行間ギャップの既定を 0 → 3 に。既存の 0 は新しい既定に引き上げる
                    foreach (var t in new[] { s.Theme }.Concat(s.Schemes.Values))
                    {
                        if (t.LineGap == 0) t.LineGap = 3;
                        if (t.TreeLineGap == 0) t.TreeLineGap = 3;
                        if (t.ViewerLineGap == 0) t.ViewerLineGap = 3;
                    }
                }
                if (s.ThemeVersion < 6)
                {
                    // v6: Markdown の色項目を追加。各配色の既定値を組み込み配色から補う（Classic 固定にならないように）
                    void FillMd(Theme t, string schemeName)
                    {
                        var src = Theme.GetBuiltIn(schemeName.Replace(" (変更あり)", ""));
                        foreach (var it in Theme.Items)
                            if (it.Group == "Markdown" && !t.Colors.ContainsKey(it.Key)) t.Colors[it.Key] = src.Colors[it.Key];
                    }
                    FillMd(s.Theme, s.CurrentScheme);
                    foreach (var kv in s.Schemes) FillMd(kv.Value, kv.Key);
                }
                if (s.ThemeVersion < 8)
                {
                    // v8: 既定マクロ（F2 → V / F で VS Code）を旧設定に 1 回だけ足す。以後は削除してもよい
                    // （v7 では版の更新より後に置いてしまい、足されていなかった）
                    foreach (var kv in new Settings().Macros) s.Macros.TryAdd(kv.Key, kv.Value);
                }
                if (s.ThemeVersion < 9)
                {
                    // v9: WinMerge 用の既定マクロ（W / M）を空いていれば足す
                    var def = new Settings().Macros;
                    foreach (var k in new[] { "W", "M" }) s.Macros.TryAdd(k, def[k]);
                }
                bool migrated = s.ThemeVersion != CurrentThemeVersion;
                s.ThemeVersion = CurrentThemeVersion;
                if (migrated) { try { s.Save(); } catch { } }   // 移行結果はすぐ書き戻す
                s.CurrentScheme = Theme.NormalizeSchemeName(s.CurrentScheme);

                s.Theme.FillMissing();
                foreach (var t in s.Schemes.Values) t.FillMissing();
                if (s.ListModes is null || s.ListModes.Count != 5)
                {
                    var d = ListMode.Defaults();
                    for (int i = 0; i < Math.Min(5, s.ListModes?.Count ?? 0); i++) d[i] = s.ListModes![i];
                    s.ListModes = d;
                }
                s.StartupListMode = Math.Clamp(s.StartupListMode, 0, 4);
                return s;
            }
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpt));
        }
        catch { }
    }
}

/// <summary>フォント 1 組（領域ごとに持つ）。</summary>
public sealed class FontSpec
{
    public string Family { get; set; } = "BIZ UDゴシック";
    public double Size { get; set; } = 16;
    public bool Bold { get; set; }
    public FontSpec Clone() => new() { Family = Family, Size = Size, Bold = Bold };
}

/// <summary>
/// 配色とフォント。WinFM2008 の「カスタマイズ › デザイン」の項目をそのまま持つ。
/// 色は "#RRGGBB"。キーは App.xaml のリソース名（…Brush を除いた名前）。
/// </summary>
public sealed class Theme
{
    public Dictionary<string, string> Colors { get; set; } = new();

    /// <summary>フォント: 情報ボックス / ファイル一覧 / ツリー / その他（ダイアログ等）</summary>
    public FontSpec InfoFont { get; set; } = new();
    public FontSpec ListFont { get; set; } = new();
    public FontSpec TreeFont { get; set; } = new();
    public FontSpec OtherFont { get; set; } = new();

    /// <summary>行間ギャップ（ドット）。ファイル一覧の各行の上下に足す。</summary>
    public int LineGap { get; set; } = 3;
    /// <summary>ツリーの行間（ドット）。</summary>
    public int TreeLineGap { get; set; } = 3;
    /// <summary>ビューアの行間（ドット）。</summary>
    public int ViewerLineGap { get; set; } = 3;

    /// <summary>タグマーク: "arrow"（▶）または "star"（*）</summary>
    public string TagMark { get; set; } = "arrow";

    /// <summary>文字の描画: "cleartype"（なめらか）/ "grayscale" / "aliased"（アンチエイリアスなし・WinFM 風にくっきり）</summary>
    public string TextRendering { get; set; } = "cleartype";

    // ------------------------------------------------------------------
    // 設定画面に出す順と名前（WinFM の「指定する部分(P)」と同じ並び）。Group は見出し用。
    // ------------------------------------------------------------------
    public sealed record ColorItem(string Key, string Label, string Group);

    public static readonly ColorItem[] Items =
    {
        // ---- ファイル一覧の文字色 ----
        new("Dir",          "ファイル フォルダ",                 "ファイル一覧"),
        new("File",         "一般のファイル",                    "ファイル一覧"),
        new("FileName",     "一般のファイル：名前（情報ボックスのファイル名）", "ファイル一覧"),
        new("Hidden",       "隠しファイル",                      "ファイル一覧"),
        new("System",       "システムファイル",                  "ファイル一覧"),
        new("ReadOnly",     "書き込み禁止ファイル",              "ファイル一覧"),
        new("Reparse",      "リパース ポイント",                 "ファイル一覧"),
        new("EncDir",       "暗号化フォルダ",                    "ファイル一覧"),
        new("EncFile",      "暗号化ファイル",                    "ファイル一覧"),
        new("CompDir",      "圧縮属性(フォルダ)",                "ファイル一覧"),
        new("CompFile",     "圧縮属性(一般のファイル)",          "ファイル一覧"),
        new("CompHidden",   "圧縮属性(隠しファイル)",            "ファイル一覧"),
        new("CompReadOnly", "圧縮属性(書き込み禁止)",            "ファイル一覧"),
        new("Tag",          "タグ付きファイル",                  "ファイル一覧"),
        new("Cursor",       "カーソル：背景色",                  "ファイル一覧"),
        new("CursorFg",     "カーソル：文字色",                  "ファイル一覧"),
        new("InactiveCursor","カーソル：背景色(非アクティブ側)", "ファイル一覧"),
        new("ListBg",       "ファイル一覧：背景色",              "ファイル一覧"),
        new("ListPath",     "ファイル一覧：パス表示",            "ファイル一覧"),
        new("GitModified",  "git：変更あり (M / ●)",             "ファイル一覧"),
        new("GitUntracked", "git：未追跡 (U)",                   "ファイル一覧"),
        new("GitAdded",     "git：追加済み (A / R)",             "ファイル一覧"),
        new("GitDeleted",   "git：削除 (D)",                     "ファイル一覧"),
        new("GitIgnored",   "git：無視 (!)",                     "ファイル一覧"),
        // ---- ファンクション（キーガイド） ----
        new("FuncLabel",    "ファンクション",                    "ファンクション"),
        new("FuncKeyFg",    "ファンクション タイトル文字色",     "ファンクション"),
        new("FuncKeyBg",    "ファンクション タイトル背景色",     "ファンクション"),
        new("FuncBg",       "ファンクション：背景色",            "ファンクション"),
        // ---- インフォメーション（情報ボックス） ----
        new("InfoTitle",    "インフォメーション タイトル",       "情報ボックス"),
        new("InfoItem",     "インフォメーション 項目",           "情報ボックス"),
        new("InfoData",     "インフォメーション データ",         "情報ボックス"),
        new("InfoTime",     "インフォメーション 時間",           "情報ボックス"),
        new("Version",      "バージョン",                        "情報ボックス"),
        new("Frame",        "フレーム",                          "情報ボックス"),
        new("InfoBg",       "情報ボックス：背景色",              "情報ボックス"),
        // ---- その他のウィンドウ（ダイアログ・メッセージ行） ----
        new("OtherFg",      "その他のウィンドウ：文字色",        "その他のウィンドウ"),
        new("OtherBg",      "その他のウィンドウ：背景色",        "その他のウィンドウ"),
        new("SelFg",        "その他のウィンドウ：選択文字",      "その他のウィンドウ"),
        new("SelBg",        "その他のウィンドウ：選択背景",      "その他のウィンドウ"),
        new("OtherDim",     "その他のウィンドウ：補助文字",      "その他のウィンドウ"),
        // ---- ツリー ----
        new("TreeLine",     "ツリー：罫線",                      "ツリー"),
        new("TreeButton",   "ツリー：ボタン色",                  "ツリー"),
        new("TreeFg",       "ツリー ビュー：文字色",             "ツリー"),
        new("TreeBg",       "ツリー ビュー：背景色",             "ツリー"),
        new("TreeSelFg",    "ツリー ビュー：選択文字",           "ツリー"),
        new("TreeSelBg",    "ツリー ビュー：選択背景",           "ツリー"),
        // ---- Markdown ビューア ----
        new("MdBg",         "Markdown：背景色",                  "Markdown"),
        new("MdFg",         "Markdown：本文",                    "Markdown"),
        new("MdHeading",    "Markdown：見出し",                  "Markdown"),
        new("MdLink",       "Markdown：リンク",                  "Markdown"),
        new("MdCode",       "Markdown：インラインコード 文字",   "Markdown"),
        new("MdCodeBg",     "Markdown：インラインコード 背景",   "Markdown"),
        new("MdQuote",      "Markdown：引用の文字",              "Markdown"),
        new("MdQuoteBg",    "Markdown：引用の背景",              "Markdown"),
        new("MdRule",       "Markdown：罫線（表・見出し下線）",  "Markdown"),
        new("MdTableHead",  "Markdown：表の見出し",              "Markdown"),
        new("MdDim",        "Markdown：補足（画像・HTML）",      "Markdown"),
    };

    public Theme Clone() => new()
    {
        Colors = new Dictionary<string, string>(Colors),
        InfoFont = InfoFont.Clone(),
        ListFont = ListFont.Clone(),
        TreeFont = TreeFont.Clone(),
        OtherFont = OtherFont.Clone(),
        LineGap = LineGap,
        TreeLineGap = TreeLineGap,
        ViewerLineGap = ViewerLineGap,
        TagMark = TagMark,
        TextRendering = TextRendering,
    };

    /// <summary>足りないキーを Classic で補完（古い設定ファイル・ユーザー保存配色対策）。</summary>
    public void FillMissing()
    {
        var def = Classic();
        foreach (var it in Items)
            if (!Colors.ContainsKey(it.Key)) Colors[it.Key] = def.Colors[it.Key];
        foreach (var f in new[] { InfoFont, ListFont, TreeFont, OtherFont })
        {
            if (string.IsNullOrWhiteSpace(f.Family)) f.Family = "BIZ UDゴシック";
            if (f.Size < 8 || f.Size > 48) f.Size = 16;
        }
        if (LineGap < 0 || LineGap > 20) LineGap = 3;
        if (TreeLineGap < 0 || TreeLineGap > 20) TreeLineGap = 3;
        if (ViewerLineGap < 0 || ViewerLineGap > 20) ViewerLineGap = 3;
        if (TagMark != "arrow" && TagMark != "star") TagMark = "arrow";
        if (TextRendering is not ("cleartype" or "grayscale" or "aliased")) TextRendering = "cleartype";
    }

    // ------------------------------------------------------------------
    // 組み込み配色
    // ------------------------------------------------------------------
    public static readonly string[] BuiltIn = { "Classic", "DOS 16色", "ダーク", "ライト", "VS Code Dark+", "VS Code Light+", "VS Code Dark Modern", "VS Code Light Modern", "VS Code Monokai", "VS Code Abyss", "ユニバーサル (ダーク)", "ユニバーサル (ライト)" };

    /// <summary>以前の名前 → 現在の名前（設定ファイルの互換）。</summary>
    public static string NormalizeSchemeName(string name) => name switch
    {
        "ダーク (Visual Studio)" => "VS Code Dark+",
        "ライト (Visual Studio)" => "VS Code Light+",
        "Solarized Dark" or "Nord" or "アンバー" or "グリーン" => "ダーク",
        _ => name,
    };

    public static Theme GetBuiltIn(string name) => name switch
    {
        "DOS 16色" => Dos16(),
        "ダーク" => Dark(),
        "ライト" => Light(),
        "VS Code Dark+" or "ダーク (Visual Studio)" => DarkVs(),
        "VS Code Light+" or "ライト (Visual Studio)" => LightVs(),
        "VS Code Dark Modern" => DarkModern(),
        "VS Code Light Modern" => LightModern(),
        "VS Code Monokai" => Monokai(),
        "VS Code Abyss" => Abyss(),
        "ユニバーサル (ダーク)" => UniversalDark(),
        "ユニバーサル (ライト)" => UniversalLight(),
        _ => Classic(),
    };

    private static Theme Make(Dictionary<string, string> colors) => new() { Colors = colors };

    /// <summary>WinFM2008 の配色 "Classic" をそのまま写したもの。既定。</summary>
    public static Theme Classic() => Make(new()
    {
        ["Dir"] = "#00FFFF", ["File"] = "#FFFF00", ["FileName"] = "#FFFFFF",
        ["Hidden"] = "#0000FF", ["System"] = "#FF00FF", ["ReadOnly"] = "#C0C000",
        ["Reparse"] = "#00C0C0", ["EncDir"] = "#008080", ["EncFile"] = "#008080",
        ["CompDir"] = "#808080", ["CompFile"] = "#C0C0C0", ["CompHidden"] = "#C0C0C0", ["CompReadOnly"] = "#808000",
        ["Tag"] = "#FF8040", ["Cursor"] = "#0000A0", ["CursorFg"] = "#FFFFFF", ["InactiveCursor"] = "#202070",
        ["ListBg"] = "#000040", ["ListPath"] = "#00FFFF",
        ["GitModified"] = "#E2C08D", ["GitUntracked"] = "#73C991", ["GitAdded"] = "#73C991", ["GitDeleted"] = "#F14C4C", ["GitIgnored"] = "#8C8C8C",
        ["FuncLabel"] = "#FFFFFF", ["FuncKeyFg"] = "#FFFFFF", ["FuncKeyBg"] = "#0000FF", ["FuncBg"] = "#000040",
        ["InfoTitle"] = "#FFFF00", ["InfoItem"] = "#00FFFF", ["InfoData"] = "#FFFFFF", ["InfoTime"] = "#00FF00",
        ["Version"] = "#808080", ["Frame"] = "#00FFFF", ["InfoBg"] = "#000040",
        ["OtherFg"] = "#FFFFFF", ["OtherBg"] = "#000040", ["SelFg"] = "#000040", ["SelBg"] = "#FFFF00", ["OtherDim"] = "#808080",
        ["TreeLine"] = "#808080", ["TreeButton"] = "#808080", ["TreeFg"] = "#FFFFFF", ["TreeBg"] = "#000040",
        ["TreeSelFg"] = "#000040", ["TreeSelBg"] = "#FFFFFF",
        ["MdBg"] = "#000040", ["MdFg"] = "#FFFFFF", ["MdHeading"] = "#FFFF00", ["MdLink"] = "#00FFFF",
        ["MdCode"] = "#FFFF00", ["MdCodeBg"] = "#1E1E1E", ["MdQuote"] = "#FFFFFF", ["MdQuoteBg"] = "#101058",
        ["MdRule"] = "#00FFFF", ["MdTableHead"] = "#FFFF00", ["MdDim"] = "#808080",
    });

    public static Theme Dos16() => Make(new()
    {
        ["Dir"] = "#55FF55", ["File"] = "#FFFF55", ["FileName"] = "#FFFFFF",
        ["Hidden"] = "#5555FF", ["System"] = "#FF55FF", ["ReadOnly"] = "#AAAA00",
        ["Reparse"] = "#55FFFF", ["EncDir"] = "#00AAAA", ["EncFile"] = "#00AAAA",
        ["CompDir"] = "#AAAAAA", ["CompFile"] = "#AAAAAA", ["CompHidden"] = "#5555AA", ["CompReadOnly"] = "#AA5500",
        ["Tag"] = "#55FFFF", ["Cursor"] = "#FF55FF", ["CursorFg"] = "#FFFFFF", ["InactiveCursor"] = "#555555",
        ["ListBg"] = "#000080", ["ListPath"] = "#FFFF55",
        ["FuncLabel"] = "#FFFF55", ["FuncKeyFg"] = "#FFFFFF", ["FuncKeyBg"] = "#0000FF", ["FuncBg"] = "#000080",
        ["InfoTitle"] = "#FFFF55", ["InfoItem"] = "#FFFF55", ["InfoData"] = "#FFFFFF", ["InfoTime"] = "#55FF55",
        ["Version"] = "#AAAAAA", ["Frame"] = "#5555FF", ["InfoBg"] = "#000080",
        ["OtherFg"] = "#FFFFFF", ["OtherBg"] = "#000080", ["SelFg"] = "#FFFFFF", ["SelBg"] = "#FF55FF", ["OtherDim"] = "#AAAAAA",
        ["TreeLine"] = "#AAAAAA", ["TreeButton"] = "#AAAAAA", ["TreeFg"] = "#FFFFFF", ["TreeBg"] = "#000080",
        ["TreeSelFg"] = "#FFFFFF", ["TreeSelBg"] = "#FF55FF",
        ["MdBg"] = "#000080", ["MdFg"] = "#FFFFFF", ["MdHeading"] = "#FFFF55", ["MdLink"] = "#55FFFF",
        ["MdCode"] = "#FFFF55", ["MdCodeBg"] = "#000040", ["MdQuote"] = "#AAAAAA", ["MdQuoteBg"] = "#0000A0",
        ["MdRule"] = "#5555FF", ["MdTableHead"] = "#FFFF55", ["MdDim"] = "#AAAAAA",
    });

    public static Theme Dark() => Make(new()
    {
        ["Dir"] = "#61AFEF", ["File"] = "#E5C07B", ["FileName"] = "#C0C0C0",
        ["Hidden"] = "#5C6370", ["System"] = "#C678DD", ["ReadOnly"] = "#D19A66",
        ["Reparse"] = "#56B6C2", ["EncDir"] = "#2A9D8F", ["EncFile"] = "#2A9D8F",
        ["CompDir"] = "#7F848E", ["CompFile"] = "#9DA5B4", ["CompHidden"] = "#4B5263", ["CompReadOnly"] = "#A0783C",
        ["Tag"] = "#E06C75", ["Cursor"] = "#3E4451", ["CursorFg"] = "#FFFFFF", ["InactiveCursor"] = "#2C313A",
        ["ListBg"] = "#000000", ["ListPath"] = "#98C379",
        ["FuncLabel"] = "#ABB2BF", ["FuncKeyFg"] = "#FFFFFF", ["FuncKeyBg"] = "#3E4451", ["FuncBg"] = "#000000",
        ["InfoTitle"] = "#E5C07B", ["InfoItem"] = "#98C379", ["InfoData"] = "#C0C0C0", ["InfoTime"] = "#98C379",
        ["Version"] = "#5C6370", ["Frame"] = "#3E4451", ["InfoBg"] = "#000000",
        ["OtherFg"] = "#C0C0C0", ["OtherBg"] = "#000000", ["SelFg"] = "#FFFFFF", ["SelBg"] = "#3E4451", ["OtherDim"] = "#5C6370",
        ["TreeLine"] = "#3E4451", ["TreeButton"] = "#5C6370", ["TreeFg"] = "#C0C0C0", ["TreeBg"] = "#000000",
        ["TreeSelFg"] = "#FFFFFF", ["TreeSelBg"] = "#3E4451",
        ["MdBg"] = "#1E1E1E", ["MdFg"] = "#D4D4D4", ["MdHeading"] = "#569CD6", ["MdLink"] = "#3794FF",
        ["MdCode"] = "#CE9178", ["MdCodeBg"] = "#2D2D2D", ["MdQuote"] = "#9DA5B4", ["MdQuoteBg"] = "#262626",
        ["MdRule"] = "#3E4451", ["MdTableHead"] = "#E5C07B", ["MdDim"] = "#5C6370",
    });

    public static Theme Light() => Make(new()
    {
        ["Dir"] = "#006400", ["File"] = "#7A5A00", ["FileName"] = "#000000",
        ["Hidden"] = "#8080C0", ["System"] = "#A000A0", ["ReadOnly"] = "#806000",
        ["Reparse"] = "#007A8A", ["EncDir"] = "#008080", ["EncFile"] = "#008080",
        ["CompDir"] = "#606060", ["CompFile"] = "#808080", ["CompHidden"] = "#A0A0C0", ["CompReadOnly"] = "#806000",
        ["Tag"] = "#C00000", ["Cursor"] = "#CCE4FF", ["CursorFg"] = "#000000", ["InactiveCursor"] = "#E6E6E6",
        ["ListBg"] = "#FFFFFF", ["ListPath"] = "#404040",
        ["FuncLabel"] = "#404040", ["FuncKeyFg"] = "#000000", ["FuncKeyBg"] = "#C0D0FF", ["FuncBg"] = "#FFFFFF",
        ["InfoTitle"] = "#000000", ["InfoItem"] = "#404040", ["InfoData"] = "#000000", ["InfoTime"] = "#006400",
        ["Version"] = "#808080", ["Frame"] = "#808080", ["InfoBg"] = "#FFFFFF",
        ["OtherFg"] = "#000000", ["OtherBg"] = "#FFFFFF", ["SelFg"] = "#000000", ["SelBg"] = "#CCE4FF", ["OtherDim"] = "#808080",
        ["TreeLine"] = "#C0C0C0", ["TreeButton"] = "#808080", ["TreeFg"] = "#000000", ["TreeBg"] = "#FFFFFF",
        ["TreeSelFg"] = "#000000", ["TreeSelBg"] = "#CCE4FF",
        ["MdBg"] = "#FFFFFF", ["MdFg"] = "#000000", ["MdHeading"] = "#0A3D91", ["MdLink"] = "#0066CC",
        ["MdCode"] = "#A31515", ["MdCodeBg"] = "#F3F3F3", ["MdQuote"] = "#505050", ["MdQuoteBg"] = "#F0F4FA",
        ["MdRule"] = "#C0C0C0", ["MdTableHead"] = "#0A3D91", ["MdDim"] = "#808080",
    });

    /// <summary>VS Code の「Dark (Visual Studio)」= Dark+ の配色（#1E1E1E / #252526 / 選択 #264F78 / 強調 #569CD6）。</summary>
    public static Theme DarkVs() => Make(new()
    {
        ["Dir"] = "#DCDCAA", ["File"] = "#D4D4D4", ["FileName"] = "#D4D4D4",
        ["Hidden"] = "#6A6A6A", ["System"] = "#C586C0", ["ReadOnly"] = "#CE9178",
        ["Reparse"] = "#4EC9B0", ["EncDir"] = "#4EC9B0", ["EncFile"] = "#4EC9B0",
        ["CompDir"] = "#9B9B9B", ["CompFile"] = "#9B9B9B", ["CompHidden"] = "#5A5A5A", ["CompReadOnly"] = "#A0783C",
        ["Tag"] = "#F44747", ["Cursor"] = "#264F78", ["CursorFg"] = "#FFFFFF", ["InactiveCursor"] = "#3A3D41",
        ["ListBg"] = "#1E1E1E", ["ListPath"] = "#569CD6",
        ["FuncLabel"] = "#CCCCCC", ["FuncKeyFg"] = "#FFFFFF", ["FuncKeyBg"] = "#007ACC", ["FuncBg"] = "#252526",
        ["InfoTitle"] = "#569CD6", ["InfoItem"] = "#9CDCFE", ["InfoData"] = "#D4D4D4", ["InfoTime"] = "#B5CEA8",
        ["Version"] = "#858585", ["Frame"] = "#3F3F46", ["InfoBg"] = "#252526",
        ["OtherFg"] = "#D4D4D4", ["OtherBg"] = "#252526", ["SelFg"] = "#FFFFFF", ["SelBg"] = "#264F78", ["OtherDim"] = "#858585",
        ["TreeLine"] = "#3F3F46", ["TreeButton"] = "#858585", ["TreeFg"] = "#D4D4D4", ["TreeBg"] = "#252526",
        ["TreeSelFg"] = "#FFFFFF", ["TreeSelBg"] = "#264F78",
        ["GitModified"] = "#E2C08D", ["GitUntracked"] = "#73C991", ["GitAdded"] = "#73C991", ["GitDeleted"] = "#F14C4C", ["GitIgnored"] = "#8C8C8C",
        ["MdBg"] = "#1E1E1E", ["MdFg"] = "#D4D4D4", ["MdHeading"] = "#569CD6", ["MdLink"] = "#3794FF",
        ["MdCode"] = "#CE9178", ["MdCodeBg"] = "#2D2D2D", ["MdQuote"] = "#9CDCFE", ["MdQuoteBg"] = "#262626",
        ["MdRule"] = "#3F3F46", ["MdTableHead"] = "#DCDCAA", ["MdDim"] = "#858585",
    });

    /// <summary>VS Code の「Light (Visual Studio)」= Light+ の配色（白 / #F3F3F3 / 選択 #CCE8FF / 強調 #0000FF）。</summary>
    public static Theme LightVs() => Make(new()
    {
        ["Dir"] = "#0000FF", ["File"] = "#000000", ["FileName"] = "#000000",
        ["Hidden"] = "#A0A0A0", ["System"] = "#AF00DB", ["ReadOnly"] = "#A31515",
        ["Reparse"] = "#267F99", ["EncDir"] = "#267F99", ["EncFile"] = "#267F99",
        ["CompDir"] = "#606060", ["CompFile"] = "#606060", ["CompHidden"] = "#B0B0B0", ["CompReadOnly"] = "#806000",
        ["Tag"] = "#E51400", ["Cursor"] = "#CCE8FF", ["CursorFg"] = "#000000", ["InactiveCursor"] = "#E5E5E5",
        ["ListBg"] = "#FFFFFF", ["ListPath"] = "#0000FF",
        ["FuncLabel"] = "#1E1E1E", ["FuncKeyFg"] = "#FFFFFF", ["FuncKeyBg"] = "#007ACC", ["FuncBg"] = "#F3F3F3",
        ["InfoTitle"] = "#0000FF", ["InfoItem"] = "#001080", ["InfoData"] = "#000000", ["InfoTime"] = "#098658",
        ["Version"] = "#808080", ["Frame"] = "#CCCEDB", ["InfoBg"] = "#F3F3F3",
        ["OtherFg"] = "#1E1E1E", ["OtherBg"] = "#F3F3F3", ["SelFg"] = "#000000", ["SelBg"] = "#CCE8FF", ["OtherDim"] = "#808080",
        ["TreeLine"] = "#CCCEDB", ["TreeButton"] = "#808080", ["TreeFg"] = "#1E1E1E", ["TreeBg"] = "#F3F3F3",
        ["TreeSelFg"] = "#000000", ["TreeSelBg"] = "#CCE8FF",
        ["GitModified"] = "#895503", ["GitUntracked"] = "#007100", ["GitAdded"] = "#007100", ["GitDeleted"] = "#AD0707", ["GitIgnored"] = "#8E8E90",
        ["MdBg"] = "#FFFFFF", ["MdFg"] = "#1E1E1E", ["MdHeading"] = "#0000FF", ["MdLink"] = "#006AB1",
        ["MdCode"] = "#A31515", ["MdCodeBg"] = "#F3F3F3", ["MdQuote"] = "#505050", ["MdQuoteBg"] = "#F0F4FA",
        ["MdRule"] = "#CCCEDB", ["MdTableHead"] = "#0000FF", ["MdDim"] = "#808080",
    });

    /// <summary>VS Code の既定テーマ「Dark Modern」（#1F1F1F / サイドバー #181818 / アクセント #0078D4）。</summary>
    public static Theme DarkModern() => Make(new()
    {
        ["Dir"] = "#DCDCAA", ["File"] = "#CCCCCC", ["FileName"] = "#CCCCCC",
        ["Hidden"] = "#6E6E6E", ["System"] = "#C586C0", ["ReadOnly"] = "#CE9178",
        ["Reparse"] = "#4EC9B0", ["EncDir"] = "#4EC9B0", ["EncFile"] = "#4EC9B0",
        ["CompDir"] = "#9D9D9D", ["CompFile"] = "#9D9D9D", ["CompHidden"] = "#5A5A5A", ["CompReadOnly"] = "#A0783C",
        ["Tag"] = "#F85149", ["Cursor"] = "#04395E", ["CursorFg"] = "#FFFFFF", ["InactiveCursor"] = "#37373D",
        ["ListBg"] = "#1F1F1F", ["ListPath"] = "#4DAAFC",
        ["FuncLabel"] = "#CCCCCC", ["FuncKeyFg"] = "#FFFFFF", ["FuncKeyBg"] = "#0078D4", ["FuncBg"] = "#181818",
        ["InfoTitle"] = "#4DAAFC", ["InfoItem"] = "#9CDCFE", ["InfoData"] = "#CCCCCC", ["InfoTime"] = "#B5CEA8",
        ["Version"] = "#8B949E", ["Frame"] = "#2B2B2B", ["InfoBg"] = "#181818",
        ["OtherFg"] = "#CCCCCC", ["OtherBg"] = "#181818", ["SelFg"] = "#FFFFFF", ["SelBg"] = "#04395E", ["OtherDim"] = "#8B949E",
        ["TreeLine"] = "#2B2B2B", ["TreeButton"] = "#8B949E", ["TreeFg"] = "#CCCCCC", ["TreeBg"] = "#181818",
        ["TreeSelFg"] = "#FFFFFF", ["TreeSelBg"] = "#04395E",
        ["GitModified"] = "#E2C08D", ["GitUntracked"] = "#73C991", ["GitAdded"] = "#73C991", ["GitDeleted"] = "#F14C4C", ["GitIgnored"] = "#8C8C8C",
        ["MdBg"] = "#1F1F1F", ["MdFg"] = "#CCCCCC", ["MdHeading"] = "#4DAAFC", ["MdLink"] = "#4DAAFC",
        ["MdCode"] = "#CE9178", ["MdCodeBg"] = "#2A2A2A", ["MdQuote"] = "#9CDCFE", ["MdQuoteBg"] = "#262626",
        ["MdRule"] = "#2B2B2B", ["MdTableHead"] = "#DCDCAA", ["MdDim"] = "#8B949E",
    });

    /// <summary>VS Code の既定テーマ「Light Modern」（白 / サイドバー #F8F8F8 / アクセント #005FB8）。</summary>
    public static Theme LightModern() => Make(new()
    {
        ["Dir"] = "#0000FF", ["File"] = "#3B3B3B", ["FileName"] = "#3B3B3B",
        ["Hidden"] = "#A0A0A0", ["System"] = "#AF00DB", ["ReadOnly"] = "#A31515",
        ["Reparse"] = "#267F99", ["EncDir"] = "#267F99", ["EncFile"] = "#267F99",
        ["CompDir"] = "#616161", ["CompFile"] = "#616161", ["CompHidden"] = "#B8B8B8", ["CompReadOnly"] = "#806000",
        ["Tag"] = "#E51400", ["Cursor"] = "#E8E8E8", ["CursorFg"] = "#000000", ["InactiveCursor"] = "#F0F0F0",
        ["ListBg"] = "#FFFFFF", ["ListPath"] = "#005FB8",
        ["FuncLabel"] = "#3B3B3B", ["FuncKeyFg"] = "#FFFFFF", ["FuncKeyBg"] = "#005FB8", ["FuncBg"] = "#F8F8F8",
        ["InfoTitle"] = "#005FB8", ["InfoItem"] = "#001080", ["InfoData"] = "#3B3B3B", ["InfoTime"] = "#098658",
        ["Version"] = "#8B949E", ["Frame"] = "#E5E5E5", ["InfoBg"] = "#F8F8F8",
        ["OtherFg"] = "#3B3B3B", ["OtherBg"] = "#F8F8F8", ["SelFg"] = "#000000", ["SelBg"] = "#E8E8E8", ["OtherDim"] = "#8B949E",
        ["TreeLine"] = "#E5E5E5", ["TreeButton"] = "#8B949E", ["TreeFg"] = "#3B3B3B", ["TreeBg"] = "#F8F8F8",
        ["TreeSelFg"] = "#000000", ["TreeSelBg"] = "#E8E8E8",
        ["GitModified"] = "#895503", ["GitUntracked"] = "#007100", ["GitAdded"] = "#007100", ["GitDeleted"] = "#AD0707", ["GitIgnored"] = "#8E8E90",
        ["MdBg"] = "#FFFFFF", ["MdFg"] = "#3B3B3B", ["MdHeading"] = "#005FB8", ["MdLink"] = "#005FB8",
        ["MdCode"] = "#A31515", ["MdCodeBg"] = "#F3F3F3", ["MdQuote"] = "#616161", ["MdQuoteBg"] = "#F3F3F3",
        ["MdRule"] = "#E5E5E5", ["MdTableHead"] = "#005FB8", ["MdDim"] = "#8B949E",
    });

    /// <summary>VS Code の「Monokai」（#272822 / 本文 #F8F8F2 / 強調 #A6E22E #F92672 #E6DB74 #66D9EF）。</summary>
    public static Theme Monokai() => Make(new()
    {
        ["Dir"] = "#66D9EF", ["File"] = "#E6DB74", ["FileName"] = "#F8F8F2",
        ["Hidden"] = "#75715E", ["System"] = "#AE81FF", ["ReadOnly"] = "#FD971F",
        ["Reparse"] = "#A6E22E", ["EncDir"] = "#A6E22E", ["EncFile"] = "#A6E22E",
        ["CompDir"] = "#90908A", ["CompFile"] = "#90908A", ["CompHidden"] = "#5E5D52", ["CompReadOnly"] = "#A0783C",
        ["Tag"] = "#F92672", ["Cursor"] = "#49483E", ["CursorFg"] = "#F8F8F2", ["InactiveCursor"] = "#3E3D32",
        ["ListBg"] = "#272822", ["ListPath"] = "#A6E22E",
        ["FuncLabel"] = "#F8F8F2", ["FuncKeyFg"] = "#272822", ["FuncKeyBg"] = "#A6E22E", ["FuncBg"] = "#1E1F1C",
        ["InfoTitle"] = "#E6DB74", ["InfoItem"] = "#66D9EF", ["InfoData"] = "#F8F8F2", ["InfoTime"] = "#A6E22E",
        ["Version"] = "#75715E", ["Frame"] = "#414339", ["InfoBg"] = "#1E1F1C",
        ["OtherFg"] = "#F8F8F2", ["OtherBg"] = "#1E1F1C", ["SelFg"] = "#F8F8F2", ["SelBg"] = "#49483E", ["OtherDim"] = "#75715E",
        ["TreeLine"] = "#414339", ["TreeButton"] = "#90908A", ["TreeFg"] = "#F8F8F2", ["TreeBg"] = "#1E1F1C",
        ["TreeSelFg"] = "#F8F8F2", ["TreeSelBg"] = "#49483E",
        ["GitModified"] = "#E6DB74", ["GitUntracked"] = "#A6E22E", ["GitAdded"] = "#A6E22E", ["GitDeleted"] = "#F92672", ["GitIgnored"] = "#75715E",
        ["MdBg"] = "#272822", ["MdFg"] = "#F8F8F2", ["MdHeading"] = "#A6E22E", ["MdLink"] = "#66D9EF",
        ["MdCode"] = "#E6DB74", ["MdCodeBg"] = "#3E3D32", ["MdQuote"] = "#90908A", ["MdQuoteBg"] = "#3E3D32",
        ["MdRule"] = "#414339", ["MdTableHead"] = "#E6DB74", ["MdDim"] = "#75715E",
    });

    /// <summary>VS Code の「Abyss」（#000C18 / 本文 #6688CC / 強調 #DDBB88 #80A2FF）。</summary>
    public static Theme Abyss() => Make(new()
    {
        ["Dir"] = "#80A2FF", ["File"] = "#DDBB88", ["FileName"] = "#6688CC",
        ["Hidden"] = "#384887", ["System"] = "#9966B8", ["ReadOnly"] = "#CC6666",
        ["Reparse"] = "#22AAAA", ["EncDir"] = "#22AAAA", ["EncFile"] = "#22AAAA",
        ["CompDir"] = "#5577AA", ["CompFile"] = "#5577AA", ["CompHidden"] = "#2E3C6E", ["CompReadOnly"] = "#A0783C",
        ["Tag"] = "#FF6666", ["Cursor"] = "#082050", ["CursorFg"] = "#FFFFFF", ["InactiveCursor"] = "#061634",
        ["ListBg"] = "#000C18", ["ListPath"] = "#80A2FF",
        ["FuncLabel"] = "#6688CC", ["FuncKeyFg"] = "#000C18", ["FuncKeyBg"] = "#DDBB88", ["FuncBg"] = "#051336",
        ["InfoTitle"] = "#DDBB88", ["InfoItem"] = "#80A2FF", ["InfoData"] = "#6688CC", ["InfoTime"] = "#22AA44",
        ["Version"] = "#384887", ["Frame"] = "#2B3F6B", ["InfoBg"] = "#051336",
        ["OtherFg"] = "#6688CC", ["OtherBg"] = "#051336", ["SelFg"] = "#FFFFFF", ["SelBg"] = "#082050", ["OtherDim"] = "#384887",
        ["TreeLine"] = "#2B3F6B", ["TreeButton"] = "#5577AA", ["TreeFg"] = "#6688CC", ["TreeBg"] = "#051336",
        ["TreeSelFg"] = "#FFFFFF", ["TreeSelBg"] = "#082050",
        ["GitModified"] = "#DDBB88", ["GitUntracked"] = "#22AA44", ["GitAdded"] = "#22AA44", ["GitDeleted"] = "#FF6666", ["GitIgnored"] = "#384887",
        ["MdBg"] = "#000C18", ["MdFg"] = "#6688CC", ["MdHeading"] = "#DDBB88", ["MdLink"] = "#80A2FF",
        ["MdCode"] = "#DDBB88", ["MdCodeBg"] = "#082050", ["MdQuote"] = "#5577AA", ["MdQuoteBg"] = "#061634",
        ["MdRule"] = "#2B3F6B", ["MdTableHead"] = "#DDBB88", ["MdDim"] = "#384887",
    });

    /// <summary>
    /// 色覚多様性に配慮した配色（ダーク）。赤と緑を対にせず、青 #56B4E9 / オレンジ #E69F00 / 黄 #F0E442 / 青 #0072B2 と
    /// 輝度差で区別する（Okabe–Ito パレット）。git 列は M=オレンジ、U/A=空色、D=紫青、!=灰。
    /// </summary>
    public static Theme UniversalDark() => Make(new()
    {
        ["Dir"] = "#56B4E9", ["File"] = "#FFFFFF", ["FileName"] = "#FFFFFF",
        ["Hidden"] = "#8A8A8A", ["System"] = "#CC79A7", ["ReadOnly"] = "#E69F00",
        ["Reparse"] = "#56B4E9", ["EncDir"] = "#56B4E9", ["EncFile"] = "#56B4E9",
        ["CompDir"] = "#9FCDE6", ["CompFile"] = "#C8C8C8", ["CompHidden"] = "#6E6E6E", ["CompReadOnly"] = "#B98200",
        ["Tag"] = "#F0E442", ["Cursor"] = "#0050A0", ["CursorFg"] = "#FFFFFF", ["InactiveCursor"] = "#2E2E2E",
        ["ListBg"] = "#000000", ["ListPath"] = "#56B4E9",
        ["FuncLabel"] = "#FFFFFF", ["FuncKeyFg"] = "#000000", ["FuncKeyBg"] = "#F0E442", ["FuncBg"] = "#101010",
        ["InfoTitle"] = "#F0E442", ["InfoItem"] = "#56B4E9", ["InfoData"] = "#FFFFFF", ["InfoTime"] = "#FFFFFF",
        ["Version"] = "#8A8A8A", ["Frame"] = "#56B4E9", ["InfoBg"] = "#101010",
        ["OtherFg"] = "#FFFFFF", ["OtherBg"] = "#101010", ["SelFg"] = "#000000", ["SelBg"] = "#F0E442", ["OtherDim"] = "#8A8A8A",
        ["TreeLine"] = "#6E6E6E", ["TreeButton"] = "#9FCDE6", ["TreeFg"] = "#FFFFFF", ["TreeBg"] = "#101010",
        ["TreeSelFg"] = "#000000", ["TreeSelBg"] = "#F0E442",
        ["GitModified"] = "#E69F00", ["GitUntracked"] = "#56B4E9", ["GitAdded"] = "#56B4E9", ["GitDeleted"] = "#CC79A7", ["GitIgnored"] = "#8A8A8A",
        ["MdBg"] = "#000000", ["MdFg"] = "#FFFFFF", ["MdHeading"] = "#F0E442", ["MdLink"] = "#56B4E9",
        ["MdCode"] = "#E69F00", ["MdCodeBg"] = "#1A1A1A", ["MdQuote"] = "#C8C8C8", ["MdQuoteBg"] = "#1A1A1A",
        ["MdRule"] = "#56B4E9", ["MdTableHead"] = "#F0E442", ["MdDim"] = "#8A8A8A",
    });

    /// <summary>色覚多様性に配慮した配色（ライト）。白地に青 #0072B2 / バーミリオン #D55E00 / 黒 を軸にし、赤緑に頼らない。</summary>
    public static Theme UniversalLight() => Make(new()
    {
        ["Dir"] = "#0072B2", ["File"] = "#000000", ["FileName"] = "#000000",
        ["Hidden"] = "#8A8A8A", ["System"] = "#CC79A7", ["ReadOnly"] = "#D55E00",
        ["Reparse"] = "#0072B2", ["EncDir"] = "#0072B2", ["EncFile"] = "#0072B2",
        ["CompDir"] = "#4A90C2", ["CompFile"] = "#505050", ["CompHidden"] = "#B0B0B0", ["CompReadOnly"] = "#A04600",
        ["Tag"] = "#D55E00", ["Cursor"] = "#FFE680", ["CursorFg"] = "#000000", ["InactiveCursor"] = "#E8E8E8",
        ["ListBg"] = "#FFFFFF", ["ListPath"] = "#0072B2",
        ["FuncLabel"] = "#000000", ["FuncKeyFg"] = "#FFFFFF", ["FuncKeyBg"] = "#0072B2", ["FuncBg"] = "#F2F2F2",
        ["InfoTitle"] = "#0072B2", ["InfoItem"] = "#D55E00", ["InfoData"] = "#000000", ["InfoTime"] = "#000000",
        ["Version"] = "#8A8A8A", ["Frame"] = "#0072B2", ["InfoBg"] = "#F2F2F2",
        ["OtherFg"] = "#000000", ["OtherBg"] = "#F2F2F2", ["SelFg"] = "#000000", ["SelBg"] = "#FFE680", ["OtherDim"] = "#8A8A8A",
        ["TreeLine"] = "#B0B0B0", ["TreeButton"] = "#4A90C2", ["TreeFg"] = "#000000", ["TreeBg"] = "#F2F2F2",
        ["TreeSelFg"] = "#000000", ["TreeSelBg"] = "#FFE680",
        ["GitModified"] = "#D55E00", ["GitUntracked"] = "#0072B2", ["GitAdded"] = "#0072B2", ["GitDeleted"] = "#CC79A7", ["GitIgnored"] = "#8A8A8A",
        ["MdBg"] = "#FFFFFF", ["MdFg"] = "#000000", ["MdHeading"] = "#0072B2", ["MdLink"] = "#0072B2",
        ["MdCode"] = "#D55E00", ["MdCodeBg"] = "#F2F2F2", ["MdQuote"] = "#505050", ["MdQuoteBg"] = "#F2F2F2",
        ["MdRule"] = "#0072B2", ["MdTableHead"] = "#0072B2", ["MdDim"] = "#8A8A8A",
    });




}
