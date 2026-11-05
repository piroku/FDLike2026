using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using FDLike2026.Services;

namespace FDLike2026.Views;

/// <summary>簡易テキスト / HEX ビューア（先頭 2MB まで表示）。</summary>
public partial class ViewerWindow : Window
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private readonly string _path;
    private readonly string _editor;
    private readonly byte[] _bytes;
    private bool _hex;
    private bool _markdown;          // いま Markdown 整形表示か
    private bool _isMarkdownFile;    // .md / .markdown
    private string? _codeLang;       // 拡張子から決めたソースの言語（json, cs, vb …）。null なら素のテキスト
    private bool _code;              // いまソース色分け表示か
    private bool _plainOnly;         // 整形 / 色分けに失敗したら素のテキストに固定（再帰防止）
    /// <summary>メイン画面の Settings（起動時に MainWindow が設定）。別インスタンスで保存すると他の設定を巻き戻すので共有する。</summary>
    public static Settings? AppSettings { get; set; }
    private static Settings? _settingsCache;
    private static Settings Cfg => AppSettings ?? (_settingsCache ??= Settings.Load());
    private bool _wrap = Cfg.ViewerWrap;   // 折り返し（W で切替、設定に記憶）

    // ---- PDF / HTML（WebView2） ----
    private static readonly HashSet<string> WebExts = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".html", ".htm", ".svg", ".mht", ".mhtml" };
    private bool _web;

    // ---- CSV / TSV ----
    private bool _isTableFile;      // .csv / .tsv
    private bool _table;            // いま表表示か
    private bool _tableHeader = true;   // 先頭行を見出しにする
    private List<string[]>? _rows;  // 解析済み

    // ---- 画像 ----
    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib", ".ico", ".webp", ".tif", ".tiff", ".wdp", ".jxr" };
    private bool _image;                 // 画像モード
    private string _imgPath = "";        // いま表示中の画像（←→ でフォルダ内を移動するので _path と別）
    private BitmapSource? _bitmap;
    private double _zoom = 1;            // 1 = 等倍
    private bool _fit = true;            // ウィンドウに合わせる
    private List<string> _siblings = new();
    private int _siblingIndex = -1;
    private Point _dragStart; private double _dragH, _dragV; private bool _dragging;

    /// <summary>任意のテキストを表示する（書庫の一覧など）。</summary>
    public ViewerWindow(Window owner, string title, string text, bool isText)
    {
        InitializeComponent();
        Owner = owner;
        _path = "";
        _editor = "";
        _bytes = Array.Empty<byte>();
        Header.Text = title;
        Body.Text = text;
    }

    /// <summary>grep の結果から: 指定行へジャンプし、検索語を強調。テキスト表示で開く（M で整形 / 色分けへ）。</summary>
    public ViewerWindow(Window owner, string path, string editor, int line, string? search, bool regex, bool caseSensitive)
        : this(owner, path, editor, OpenFrom.Grep)
    {
        if (_image || _hex) return;
        if (!string.IsNullOrEmpty(search)) SetSearch(search, regex, caseSensitive);
        // grep から: 行番号付きのソース表示で開き、一致行に ► マーク、該当行に帯
        _markdown = false; _code = true; _codeLang ??= (_isMarkdownFile ? "md" : "text");
        _targetLine = line;
        if (_isMarkdownFile) KeyHint.Text = "M: Markdown/テキスト切替　/: 検索  n/N: 次/前　H: HEX　E: エディタ　Esc/Q: 閉じる";
        else KeyHint.Text = $"M: 色分け({_codeLang})/テキスト切替　/: 検索  n/N: 次/前　H: HEX　E: エディタ　W: 折返し　Esc/Q: 閉じる";
        Render();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            ScrollToAnchor();
            Title = $"FDLike2026 - ビューア  {_targetLine} 行目" + (_search is not null ? $"　検索: {_searchText}（n: 次へ）" : "");
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private int _targetLine;
    private bool _docText;           // Office 文書 / PDF から取り出したテキストを表示中
    private System.Windows.Documents.Inline? _anchor;

    private void ScrollToAnchor()
    {
        try
        {
            if (_anchor is null) return;
            // 文書を差し替えた直後はレイアウトが終わっていないので、先に確定させてから位置を出す
            MdView.UpdateLayout();
            if (MdView.Template.FindName("PART_ContentHost", MdView) is ScrollViewer sv)
            {
                var rect = _anchor.ContentStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);
                // rect はビューポート基準。現在のオフセットを足して文書内の絶対位置にし、画面の上 1/3 に置く
                double y = sv.VerticalOffset + rect.Top;
                if (!double.IsNaN(y) && !double.IsInfinity(y))
                {
                    sv.ScrollToVerticalOffset(Math.Max(0, y - sv.ViewportHeight / 3));
                    return;
                }
            }
            _anchor.BringIntoView();
        }
        catch { }
    }

    /// <summary>どこから開かれたか（grep からは PDF もテキスト抽出で開く）。</summary>
    public enum OpenFrom { List, Grep }

    public ViewerWindow(Window owner, string path, string editor) : this(owner, path, editor, OpenFrom.List) { }

    public ViewerWindow(Window owner, string path, string editor, OpenFrom from)
    {
        bool fromGrep = from == OpenFrom.Grep;
        InitializeComponent();
        Owner = owner;
        _path = path;
        _editor = editor;
        Body.TextWrapping = _wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;

        // Office 文書 / PDF: 取り出したテキストを表示（grep の行番号と一致する）。
        // 通常の V では PDF は Edge で見せるので、PDF のテキスト表示は grep から来たときだけ。
        var ext0 = Path.GetExtension(path);
        if (DocumentText.IsDocument(ext0) && (fromGrep || !WebExts.Contains(ext0)))
        {
            var extracted = DocumentText.Extract(path);
            if (extracted is not null)
            {
                _bytes = System.Text.Encoding.UTF8.GetBytes(extracted);
                _docText = true;
                Header.Text = $"{path}  [{new FileInfo(path).Length:#,0} bytes]  （{DocumentText.MethodLabel(ext0)}）";
                _codeLang = "text"; _code = true;
                KeyHint.Text = "抽出テキストを表示　/: 検索  n/N: 次/前　E: 既定のアプリで開く　W: 折返し　Esc/Q: 閉じる";
                Render();
                return;
            }
        }

        if (WebExts.Contains(Path.GetExtension(path)))
        {
            bool pdf = Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
            if (pdf) _bytes = Array.Empty<byte>();
            else
            {
                // HTML / SVG は M でソース表示にも切り替えられるよう中身も読んでおく
                using var hs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var hlen = (int)Math.Min(hs.Length, MaxBytes);
                _bytes = new byte[hlen]; _ = hs.Read(_bytes, 0, hlen);
                _codeLang = "xml"; _code = true;
            }
            _web = true;
            Header.Text = $"{path}  [{new FileInfo(path).Length:#,0} bytes]";
            KeyHint.Text = (pdf ? "Edge のビューア（←→/PgUp/PgDn ページ、Ctrl+ホイール 拡大縮小、Ctrl+F 検索）　" : "Edge で表示　M: ソース表示と切替　")
                         + "E: 既定のアプリで開く　Esc/Q: 閉じる";
            Loaded += (_, _) => _ = ShowWebAsync(path);
            return;
        }
        if (ImageExts.Contains(Path.GetExtension(path)))
        {
            _bytes = Array.Empty<byte>();
            _image = true;
            KeyHint.Text = "←→/PgUp/PgDn: 前後の画像　+/-/ホイール: 拡大縮小　0: 全体表示　1: 等倍　ドラッグ: 移動　E: エディタ　Esc/Q: 閉じる";
            Loaded += (_, _) => ShowImage(path);
            return;
        }

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var len = (int)Math.Min(fs.Length, MaxBytes);
        _bytes = new byte[len];
        _ = fs.Read(_bytes, 0, len);
        var truncated = fs.Length > MaxBytes ? "（先頭 2MB のみ表示）" : "";
        Header.Text = $"{path}  [{fs.Length:#,0} bytes]{truncated}";

        _hex = LooksBinary(_bytes);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        _isMarkdownFile = !_hex && ext is ".md" or ".markdown" or ".mdown";
        _markdown = _isMarkdownFile;
        _isTableFile = !_hex && ext is ".csv" or ".tsv" or ".tab";
        _table = _isTableFile;
        if (_isTableFile) KeyHint.Text = "M: 表/テキスト切替　T: 先頭行を見出しに　/: 検索　H: HEX　E: エディタ　W: 折返し　Esc/Q: 閉じる";
        if (_isMarkdownFile) KeyHint.Text = "M: Markdown/テキスト切替　/: 検索  n/N: 次/前　H: HEX　E: エディタ　W: 折返し　Esc/Q: 閉じる";
        else if (!_hex && CodeHighlighter.FromExtension(ext) is { } lang)
        {
            _codeLang = lang; _code = true;
            KeyHint.Text = $"M: 色分け({lang})/テキスト切替　/: 検索  n/N: 次/前　H: HEX　E: エディタ　W: 折返し　Esc/Q: 閉じる";
        }
        Render();
    }

    private static bool LooksBinary(byte[] b) => TextDecoder.LooksBinary(b);

    private void Render()
    {
        bool table = _table && _isTableFile && !_hex && !_plainOnly && _search is null;
        TableView.Visibility = table ? Visibility.Visible : Visibility.Collapsed;
        if (table)
        {
            MdView.Visibility = Visibility.Collapsed; Body.Visibility = Visibility.Collapsed;
            try { RenderTable(); } catch { _plainOnly = true; Render(); }
            return;
        }
        bool md = _markdown && !_hex && !_plainOnly;
        // .md のテキスト表示も Markdown ソースとして色分けする
        // 検索中（grep から / 「/」で検索）は、色分け OFF でも行番号付き表示（言語 "text" = 白一色）にして ► マークと帯を保つ
        bool searching = _search is not null;
        bool code = !_hex && !_plainOnly && !_markdown &&
                    ((_code && _codeLang is not null) || (_isMarkdownFile && !_markdown) || searching);
        var codeLang = _code ? (_codeLang ?? "md") : (_isMarkdownFile && !searching ? "md" : "text");
        MdView.Visibility = md || code ? Visibility.Visible : Visibility.Collapsed;
        Body.Visibility = md || code ? Visibility.Collapsed : Visibility.Visible;
        if (code)
        {
            try
            {
                var font = TryFindResource("OtherFontFamily") as System.Windows.Media.FontFamily ?? new System.Windows.Media.FontFamily("BIZ UDゴシック");
                double size = TryFindResource("OtherFontSize") as double? ?? 14;
                double lh = TryFindResource("ViewerLineHeight") as double? ?? size * 1.4;
                MdView.Background = CodeHighlighter.Background;
                var text = DecodeText(_bytes);
                var cdoc = MarkdownRenderer.RenderCode(text, codeLang, font, size, lh, _search, _targetLine, out _anchor);
                ApplyWrap(cdoc, text, font, size);
                MdView.Document = cdoc;
            }
            catch { _plainOnly = true; Render(); }
            return;
        }
        MdView.Background = System.Windows.Media.Brushes.Transparent;
        if (md)
        {
            try
            {
                var font = TryFindResource("OtherFontFamily") as System.Windows.Media.FontFamily ?? new System.Windows.Media.FontFamily("BIZ UDゴシック");
                double size = TryFindResource("OtherFontSize") as double? ?? 14;
                double lh = TryFindResource("ViewerLineHeight") as double? ?? size * 1.4;
                // Markdown: 本文は折り返し、幅の広い表はその表だけ横スクロール（列幅は内容どおり）
                MarkdownRenderer.WrapTables = false;
                MdView.Background = MarkdownRenderer.PageBg;
                MarkdownRenderer.AvailableWidth = MdView.ActualWidth > 0 ? MdView.ActualWidth - 20 : ActualWidth - 60;
                _mdRenderedWidth = MarkdownRenderer.AvailableWidth;
                MdView.Document = MarkdownRenderer.Render(DecodeText(_bytes), font, size, lh);
                MdView.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            catch (Exception ex)
            {
                _plainOnly = true; Render();
                Body.Text = "（Markdown の整形に失敗したのでテキスト表示）\n" + ex.Message + "\n\n" + Body.Text;
            }
            return;
        }
        if (_hex)
        {
            var sb = new StringBuilder(_bytes.Length * 4);
            for (int off = 0; off < _bytes.Length; off += 16)
            {
                sb.Append(off.ToString("X8")).Append("  ");
                for (int i = 0; i < 16; i++)
                {
                    if (off + i < _bytes.Length) sb.Append(_bytes[off + i].ToString("X2")).Append(' ');
                    else sb.Append("   ");
                    if (i == 7) sb.Append(' ');
                }
                sb.Append(' ');
                for (int i = 0; i < 16 && off + i < _bytes.Length; i++)
                {
                    var c = _bytes[off + i];
                    sb.Append(c >= 0x20 && c < 0x7F ? (char)c : '.');
                }
                sb.AppendLine();
            }
            Body.Text = sb.ToString();
        }
        else
        {
            Body.Text = DecodeText(_bytes);
        }
    }

    private static string DecodeText(byte[] b) => TextDecoder.Decode(b);

    /// <summary>
    /// 折り返しなしのときは、いちばん長い行の実測幅を PageWidth にして横スクロールさせる。
    /// 折り返しありは PageWidth = NaN（ビューポート幅に合わせる）。
    /// </summary>
    private void ApplyWrap(System.Windows.Documents.FlowDocument doc, string text, System.Windows.Media.FontFamily font, double size)
    {
        if (_wrap)
        {
            doc.PageWidth = double.NaN;
            MdView.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            return;
        }
        try
        {
            // 文字数の多い行から 30 本だけ実測（全行測ると大きいファイルで遅い）
            var lines = text.Split('\n');
            var longest = lines.OrderByDescending(l => l.Length).Take(30);
            var tf = new System.Windows.Media.Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double max = 0;
            foreach (var l in longest)
            {
                var ft = new System.Windows.Media.FormattedText(l.TrimEnd('\r'), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, size, System.Windows.Media.Brushes.White, 1.0);
                if (ft.WidthIncludingTrailingWhitespace > max) max = ft.WidthIncludingTrailingWhitespace;
            }
            int digits = Math.Max(2, lines.Length.ToString().Length);
            double gutter = size * 0.7 * (digits + 3);               // "►1234  " ぶん
            doc.PageWidth = Math.Max(MdView.ActualWidth, max + gutter + 40);
            MdView.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        catch { doc.PageWidth = double.NaN; }
    }

    // Markdown: 幅が大きく変わったら表の列幅を取り直す
    private double _mdRenderedWidth;
    private void MdView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_markdown || MdView.Visibility != Visibility.Visible) return;
        if (Math.Abs(MdView.ActualWidth - 20 - _mdRenderedWidth) < 40) return;
        Render();
    }

    // =====================================================================
    // CSV / TSV の表表示
    // =====================================================================
    private void RenderTable()
    {
        _rows ??= ParseDelimited(DecodeText(_bytes), Path.GetExtension(_path).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? ',' : '\t');
        int cols = _rows.Count == 0 ? 0 : _rows.Max(r => r.Length);
        bool header = _tableHeader && _rows.Count > 0;
        var head = header ? _rows[0] : Array.Empty<string>();
        var data = header ? _rows.Skip(1).ToList() : _rows;

        TableView.Columns.Clear();
        for (int c = 0; c < cols; c++)
        {
            string title = c < head.Length && head[c].Length > 0 ? head[c] : ColumnName(c);
            TableView.Columns.Add(new DataGridTextColumn
            {
                Header = title,
                Binding = new System.Windows.Data.Binding($"[{c}]"),
                MaxWidth = 480,
            });
        }
        // 行の長さを列数にそろえる（短い行で例外にならないように）
        var padded = data.Select(r => r.Length == cols ? r : r.Concat(Enumerable.Repeat("", cols - r.Length)).ToArray()).ToList();
        TableView.ItemsSource = padded;
        Title = $"FDLike2026 - ビューア  {padded.Count:#,0} 行 × {cols} 列" + (header ? "（先頭行＝見出し）" : "");
    }

    private static string ColumnName(int index)
    {
        // Excel 風: A, B, …, Z, AA, AB, …
        var sb = new StringBuilder();
        int n = index;
        do { sb.Insert(0, (char)('A' + n % 26)); n = n / 26 - 1; } while (n >= 0);
        return sb.ToString();
    }

    /// <summary>RFC 4180 準拠の簡易パーサ（"" エスケープ、引用内の改行・区切り文字に対応）。</summary>
    private static List<string[]> ParseDelimited(string text, char sep)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool inQuote = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuote)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else inQuote = false;
                }
                else cell.Append(ch);
                continue;
            }
            if (ch == '"' && cell.Length == 0) { inQuote = true; continue; }
            if (ch == sep) { row.Add(cell.ToString()); cell.Clear(); continue; }
            if (ch == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; row.Add(cell.ToString()); cell.Clear(); rows.Add(row.ToArray()); row.Clear(); continue; }
            if (ch == '\n') { row.Add(cell.ToString()); cell.Clear(); rows.Add(row.ToArray()); row.Clear(); continue; }
            cell.Append(ch);
            if (rows.Count >= 200000) break;   // 上限
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row.ToArray()); }
        // 末尾の空行を落とす
        while (rows.Count > 0 && rows[^1].All(c => c.Length == 0)) rows.RemoveAt(rows.Count - 1);
        return rows;
    }

    private void TableView_LoadingRow(object sender, DataGridRowEventArgs e)
        => e.Row.Header = (e.Row.GetIndex() + 1 + (_tableHeader ? 1 : 0)).ToString();

    // =====================================================================
    // PDF / HTML ビューア（WebView2）
    // =====================================================================
    private async Task ShowWebAsync(string path)
    {
        WebHost.Visibility = Visibility.Visible;
        Body.Visibility = Visibility.Collapsed; MdView.Visibility = Visibility.Collapsed;
        try
        {
            // ユーザーデータは %LOCALAPPDATA%\FDLike2026\WebView2 に（exe のフォルダに書かない）
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FDLike2026", "WebView2");
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userData);
            await Web.EnsureCoreWebView2Async(env);
            var core = Web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = true;
            // 外部へのリンクは FDLike 内で開かず既定のブラウザへ
            core.NewWindowRequested += (_, a) => { a.Handled = true; try { FileOperations.ShellExecute(a.Uri); } catch { } };
            core.Navigate(new Uri(path).AbsoluteUri);
            // Esc / Q をこちらで拾えるように、WebView 内のキーをウィンドウへ転送
            Web.KeyDown += (_, ke) =>
            {
                if (ke.Key is Key.Escape or Key.Q) { Close(); ke.Handled = true; }
                else if (ke.Key == Key.E) { try { FileOperations.ShellExecute(_path); } catch { } ke.Handled = true; }
            };
            Web.Focus();
        }
        catch (Exception ex)
        {
            Web.Visibility = Visibility.Collapsed;
            WebMessage.Visibility = Visibility.Visible;
            WebMessage.Text = "WebView2（Edge のビューア）を起動できませんでした。\n" +
                              "WebView2 ランタイムが入っていない可能性があります。E キーで既定のアプリで開けます。\n\n" + ex.Message;
        }
    }

    // =====================================================================
    // 画像ビューア
    // =====================================================================
    private void ShowImage(string path)
    {
        _imgPath = path;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;              // ファイルをロックしない
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            _bitmap = bmp;

            bool gif = string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase);
            if (gif)
            {
                // GIF はアニメーション再生（WpfAnimatedGif）
                WpfAnimatedGif.ImageBehavior.SetAnimatedSource(Img, bmp);
                WpfAnimatedGif.ImageBehavior.SetRepeatBehavior(Img, System.Windows.Media.Animation.RepeatBehavior.Forever);
            }
            else
            {
                WpfAnimatedGif.ImageBehavior.SetAnimatedSource(Img, null);
                Img.Source = bmp;
            }

            // フォルダ内の画像一覧（←→ 用）
            var dir = Path.GetDirectoryName(path) ?? "";
            _siblings = Directory.EnumerateFiles(dir).Where(f => ImageExts.Contains(Path.GetExtension(f)))
                                 .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            _siblingIndex = _siblings.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));

            long size = new FileInfo(path).Length;
            string dpi = Math.Abs(bmp.DpiX - 96) > 0.5 ? $"  {bmp.DpiX:0}dpi" : "";
            Header.Text = $"{path}  [{bmp.PixelWidth} x {bmp.PixelHeight} px{dpi}  {bmp.Format.BitsPerPixel}bit  {size:#,0} bytes]" +
                          (_siblings.Count > 1 ? $"  ({_siblingIndex + 1}/{_siblings.Count})" : "");
            _fit = true;
            ImgScroll.Visibility = Visibility.Visible;
            Body.Visibility = Visibility.Collapsed;
            MdView.Visibility = Visibility.Collapsed;
            ApplyZoom();
        }
        catch (Exception ex)
        {
            _image = false;
            ImgScroll.Visibility = Visibility.Collapsed;
            Body.Visibility = Visibility.Visible;
            Body.Text = "画像を読み込めませんでした: " + ex.Message;
        }
    }

    /// <summary>ズーム倍率（または全体表示）に合わせて Image の表示サイズを決める。</summary>
    private void ApplyZoom()
    {
        if (_bitmap is null) return;
        double pw = _bitmap.PixelWidth, ph = _bitmap.PixelHeight;
        if (_fit)
        {
            double vw = Math.Max(1, ImgScroll.ViewportWidth > 0 ? ImgScroll.ViewportWidth : ImgScroll.ActualWidth - 2);
            double vh = Math.Max(1, ImgScroll.ViewportHeight > 0 ? ImgScroll.ViewportHeight : ImgScroll.ActualHeight - 2);
            _zoom = Math.Min(vw / pw, vh / ph);
            if (_zoom > 1) _zoom = 1;   // 小さい画像は拡大しない
        }
        Img.Width = Math.Max(1, pw * _zoom);
        Img.Height = Math.Max(1, ph * _zoom);
        ImgScroll.HorizontalScrollBarVisibility = _fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        ImgScroll.VerticalScrollBarVisibility = _fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        Title = $"FDLike2026 - ビューア  {_zoom * 100:0}%" + (_fit ? "（全体）" : "");
    }

    private void SetZoom(double z, Point? anchor = null)
    {
        if (_bitmap is null) return;
        z = Math.Clamp(z, 0.02, 32);
        // アンカー（マウス位置）を中心に拡大縮小
        double oldZoom = _zoom;
        double ax = anchor?.X ?? ImgScroll.ViewportWidth / 2, ay = anchor?.Y ?? ImgScroll.ViewportHeight / 2;
        double cx = (ImgScroll.HorizontalOffset + ax) / oldZoom, cy = (ImgScroll.VerticalOffset + ay) / oldZoom;
        _fit = false; _zoom = z;
        ApplyZoom();
        ImgScroll.UpdateLayout();
        ImgScroll.ScrollToHorizontalOffset(cx * z - ax);
        ImgScroll.ScrollToVerticalOffset(cy * z - ay);
    }

    private void StepImage(int delta)
    {
        if (_siblings.Count == 0) return;
        int i = (_siblingIndex + delta + _siblings.Count) % _siblings.Count;
        ShowImage(_siblings[i]);
    }

    private void ImgScroll_SizeChanged(object sender, SizeChangedEventArgs e) { if (_image && _fit) ApplyZoom(); }

    private void ImgScroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_image) return;
        double factor = e.Delta > 0 ? 1.25 : 0.8;
        SetZoom(_zoom * factor, e.GetPosition(ImgScroll));
        e.Handled = true;
    }

    private void Img_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_image || _fit) return;
        _dragging = true; _dragStart = e.GetPosition(ImgScroll);
        _dragH = ImgScroll.HorizontalOffset; _dragV = ImgScroll.VerticalOffset;
        ImgScroll.CaptureMouse();
    }

    private void Img_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(ImgScroll);
        ImgScroll.ScrollToHorizontalOffset(_dragH - (p.X - _dragStart.X));
        ImgScroll.ScrollToVerticalOffset(_dragV - (p.Y - _dragStart.Y));
    }

    private void Img_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false; ImgScroll.ReleaseMouseCapture();
    }

    // =====================================================================
    // カーソルキーでのスクロール（FD 風: ↑↓ 1 行、←→ 横、PgUp/PgDn 1 ページ、Home/End 先頭末尾、Space/BS ページ送り戻し）
    // =====================================================================
    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var r = FindScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
            if (r is not null) return r;
        }
        return null;
    }

    private bool ScrollKey(Key key, bool shift)
    {
        if (_image || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return false;
        ScrollViewer? sv = MdView.Visibility == Visibility.Visible ? FindScrollViewer(MdView) : FindScrollViewer(Body);
        if (sv is null) return false;
        double line = TryFindResource("ViewerLineHeight") as double? ?? 20;
        double page = Math.Max(line, sv.ViewportHeight - line);
        switch (key)
        {
            case Key.Up: sv.ScrollToVerticalOffset(sv.VerticalOffset - line); return true;
            case Key.Down: sv.ScrollToVerticalOffset(sv.VerticalOffset + line); return true;
            case Key.Left: sv.ScrollToHorizontalOffset(sv.HorizontalOffset - line * 4); return true;
            case Key.Right: sv.ScrollToHorizontalOffset(sv.HorizontalOffset + line * 4); return true;
            case Key.PageUp: case Key.Back: sv.ScrollToVerticalOffset(sv.VerticalOffset - page); return true;
            case Key.PageDown: case Key.Space: sv.ScrollToVerticalOffset(sv.VerticalOffset + page); return true;
            case Key.Home: if (shift || MdView.Visibility == Visibility.Visible) { sv.ScrollToTop(); return true; } return false;
            case Key.End: if (shift || MdView.Visibility == Visibility.Visible) { sv.ScrollToBottom(); return true; } return false;
            default: return false;
        }
    }

    private bool ImageKey(Key key)
    {
        switch (key)
        {
            case Key.Left: case Key.PageUp: case Key.Back: StepImage(-1); return true;
            case Key.Right: case Key.PageDown: case Key.Space: StepImage(1); return true;
            case Key.Home: if (_siblings.Count > 0) ShowImage(_siblings[0]); return true;
            case Key.End: if (_siblings.Count > 0) ShowImage(_siblings[^1]); return true;
            case Key.OemPlus: case Key.Add: SetZoom(_zoom * 1.25); return true;
            case Key.OemMinus: case Key.Subtract: SetZoom(_zoom * 0.8); return true;
            case Key.D0: case Key.NumPad0: case Key.F: _fit = true; ApplyZoom(); return true;
            case Key.D1: case Key.NumPad1: case Key.Z: SetZoom(1); return true;
            case Key.E: if (_imgPath.Length > 0) { FileOperations.ShellExecute(_imgPath); } return true;   // 画像は関連付けで開く
            default: return false;
        }
    }

    // =====================================================================
    // ファイル内検索（/ または Ctrl+F、n: 次へ、Shift+N: 前へ）
    // =====================================================================
    private Regex? _search;
    private string _searchText = "";

    private void SetSearch(string text, bool regex, bool caseSensitive)
    {
        _searchText = text;
        _hitLines = null;
        try { _search = new Regex(regex ? text : Regex.Escape(text), (caseSensitive ? 0 : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant); }
        catch { _search = new Regex(Regex.Escape(text), RegexOptions.IgnoreCase); }
    }

    /// <summary>テキスト表示に切り替えて指定行を画面中央あたりへ。</summary>
    private void GoToLine(int line)
    {
        if (_image || _hex) return;
        if (Body.Visibility != Visibility.Visible) return;
        if (line <= 0) return;
        Body.UpdateLayout();
        Body.Focus();
        // 行の先頭位置はテキストから直接数える（TextBox の行 API はレイアウト前だと信用できない）
        var text = Body.Text;
        int idx = 0, li = 0;
        while (li < line - 1)
        {
            int nl = text.IndexOf('\n', idx);
            if (nl < 0) break;
            idx = nl + 1; li++;
        }
        int end = text.IndexOf('\n', idx); if (end < 0) end = text.Length;
        int selLen = end - idx; while (selLen > 0 && text[idx + selLen - 1] == '\r') selLen--;
        Body.Select(idx, Math.Max(1, selLen));
        Body.ScrollToLine(Math.Max(0, li - 8));
        _jumpLine = line;
        Title = $"FDLike2026 - ビューア  {line} 行目" + (_search is not null ? $"　検索: {_searchText}（n: 次へ）" : "");
    }
    private int _jumpLine;

    private void AskSearch()
    {
        if (_image || _hex) return;
        var t = InputDialog.Show(this, "検索文字列（n: 次へ　Shift+N: 前へ）", _searchText);
        if (string.IsNullOrEmpty(t)) return;
        SetSearch(t, regex: false, caseSensitive: false);
        _hitLines = null;
        _markdown = false; _table = false;
        Render();                      // 検索中は行番号付き表示になる
        _targetLine = 0; CodeFind(+1);
    }

    /// <summary>色分け（行番号）表示のまま、一致行の間を移動する。</summary>
    private bool CodeModeActive => MdView.Visibility == Visibility.Visible && !_markdown;

    private List<int>? _hitLines;
    private void EnsureHitLines()
    {
        if (_hitLines is not null || _search is null) return;
        _hitLines = new List<int>();
        var text = DecodeText(_bytes).Replace("\r\n", "\n").Replace('\r', '\n');
        int line = 1, pos = 0;
        while (pos <= text.Length)
        {
            int nl = text.IndexOf('\n', pos);
            int end = nl < 0 ? text.Length : nl;
            try { if (_search.IsMatch(text.AsSpan(pos, end - pos).ToString())) _hitLines.Add(line); } catch { }
            if (nl < 0) break;
            pos = nl + 1; line++;
        }
    }

    private void CodeFind(int delta)
    {
        EnsureHitLines();
        if (_hitLines is null || _hitLines.Count == 0) { Title = $"FDLike2026 - ビューア  「{_searchText}」は見つかりません"; return; }
        int idx;
        if (delta > 0) { idx = _hitLines.FindIndex(l => l > _targetLine); if (idx < 0) { idx = 0; } }
        else { idx = _hitLines.FindLastIndex(l => l < _targetLine); if (idx < 0) idx = _hitLines.Count - 1; }
        _targetLine = _hitLines[idx];
        Render();
        // 文書差し替え後、レイアウト完了を待ってからスクロール
        Dispatcher.BeginInvoke(ScrollToAnchor, System.Windows.Threading.DispatcherPriority.Loaded);
        Title = $"FDLike2026 - ビューア  {_targetLine} 行目　検索: {_searchText}（{idx + 1}/{_hitLines.Count}）" + (delta > 0 && idx == 0 && _hitLines.Count > 1 ? "（先頭に戻りました）" : delta < 0 && idx == _hitLines.Count - 1 && _hitLines.Count > 1 ? "（末尾に戻りました）" : "");
    }

    private void FindNext(bool fromLineStart = false)
    {
        if (_search is null) return;
        if (_markdown) { _markdown = false; _code = true; _codeLang ??= "md"; Render(); }   // 整形表示では探せないのでソース表示へ
        if (CodeModeActive) { CodeFind(+1); return; }
        if (Body.Visibility != Visibility.Visible) { _markdown = false; _code = false; Render(); }
        var text = Body.Text;
        int start = fromLineStart || _jumpLine > 0 ? Body.SelectionStart : Body.SelectionStart + Math.Max(1, Body.SelectionLength);
        _jumpLine = 0;
        if (start > text.Length) start = 0;
        var m = _search.Match(text, start);
        if (!m.Success) { m = _search.Match(text, 0); if (!m.Success) { Title = $"FDLike2026 - ビューア  「{_searchText}」は見つかりません"; return; } Title = "FDLike2026 - ビューア  （先頭に戻りました）"; }
        else Title = $"FDLike2026 - ビューア  検索: {_searchText}";
        ShowMatch(m);
    }

    private void FindPrev()
    {
        if (_search is null) return;
        if (_markdown) { _markdown = false; _code = true; _codeLang ??= "md"; Render(); }
        if (CodeModeActive) { CodeFind(-1); return; }
        if (Body.Visibility != Visibility.Visible) { _markdown = false; _code = false; Render(); }
        var text = Body.Text;
        int before = Body.SelectionStart;
        Match? last = null;
        foreach (Match m in _search.Matches(text)) { if (m.Index < before) last = m; else break; }
        if (last is null) { var all = _search.Matches(text); if (all.Count == 0) return; last = all[^1]; Title = "FDLike2026 - ビューア  （末尾に戻りました）"; }
        else Title = $"FDLike2026 - ビューア  検索: {_searchText}";
        ShowMatch(last);
    }

    private void ShowMatch(Match m)
    {
        Body.Focus();
        Body.Select(m.Index, Math.Max(1, m.Length));
        int li = Body.GetLineIndexFromCharacterIndex(m.Index);
        Body.ScrollToLine(Math.Max(0, li - 8));
        _targetLine = li + 1;
    }

    /// <summary>
    /// 開いた直後にマウスカーソルが「起動中（〇）」や「幅変更（⇔）」のまま残ることがあるので、
    /// メイン画面側の一時カーソルを解除し、このウィンドウ上は通常の矢印にする。
    /// </summary>
    private void Window_Activated(object sender, EventArgs e)
    {
        Mouse.OverrideCursor = null;
        Cursor = Cursors.Arrow;
        Dispatcher.BeginInvoke(() => { Mouse.OverrideCursor = null; Mouse.UpdateCursor(); }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (_web)
        {
            if (e.Key is Key.Escape or Key.Q) { Close(); e.Handled = true; }
            else if (e.Key == Key.E) { try { FileOperations.ShellExecute(_path); } catch { } e.Handled = true; }
            else if (e.Key == Key.M && _bytes.Length > 0) { _web = false; WebHost.Visibility = Visibility.Collapsed; Render(); Focus(); e.Handled = true; }
            return;   // それ以外は WebView2（Edge のビューア）に任せる
        }
        if (!_web && WebExts.Contains(Path.GetExtension(_path)) && e.Key == Key.M && _bytes.Length > 0)
        {
            // ソース表示 → Edge 表示へ戻る
            _web = true; Body.Visibility = Visibility.Collapsed; MdView.Visibility = Visibility.Collapsed;
            WebHost.Visibility = Visibility.Visible; Web.Focus(); e.Handled = true; return;
        }
        if (TableView.Visibility == Visibility.Visible && e.Key is not (Key.Escape or Key.Q or Key.M or Key.T or Key.E or Key.H)) return;   // 表は DataGrid のキー操作に任せる
        if (!_image && ScrollKey(e.Key, shift)) { e.Handled = true; return; }
        if (!_image && !_hex)
        {
            if ((e.Key == Key.F && ctrl) || e.Key == Key.OemQuestion || e.Key == Key.Divide) { AskSearch(); e.Handled = true; return; }
            if (e.Key == Key.N && _search is not null) { if (shift) FindPrev(); else FindNext(); e.Handled = true; return; }
            if (e.Key == Key.F3 && _search is not null) { if (shift) FindPrev(); else FindNext(); e.Handled = true; return; }
        }
        if (_image && e.Key is not (Key.Escape or Key.Q))
        {
            if (ImageKey(e.Key)) e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Q: Close(); e.Handled = true; break;
            case Key.H: if (_bytes.Length > 0 && !_docText) { _hex = !_hex; Render(); } e.Handled = true; break;
            case Key.T when _isTableFile && _table:
                _tableHeader = !_tableHeader; Render(); e.Handled = true; break;
            case Key.M:
                if (_isTableFile) { _table = !_table; _hex = false; Render(); }
                else if (_isMarkdownFile) { _markdown = !_markdown; _hex = false; Render(); }
                else if (_codeLang is not null) { _code = !_code; _hex = false; Render(); }
                // 切り替え後も同じ行を示す（レイアウト確定後に位置合わせ）
                if (_targetLine > 0)
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (Body.Visibility == Visibility.Visible) GoToLine(_targetLine);
                        else if (MdView.Visibility == Visibility.Visible && !_markdown) ScrollToAnchor();
                    }, System.Windows.Threading.DispatcherPriority.Loaded);
                e.Handled = true; break;
            case Key.W:
                _wrap = !_wrap;
                Cfg.ViewerWrap = _wrap; try { Cfg.Save(); } catch { }
                Body.TextWrapping = _wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
                if (MdView.Visibility == Visibility.Visible && !_markdown && MdView.Document is { } d)
                {
                    ApplyWrap(d, DecodeText(_bytes), d.FontFamily, d.FontSize);
                    if (_targetLine > 0) Dispatcher.BeginInvoke(ScrollToAnchor, System.Windows.Threading.DispatcherPriority.Loaded);
                }

                Title = Title.Replace("　折り返し: あり", "").Replace("　折り返し: なし", "") + (_wrap ? "　折り返し: あり" : "　折り返し: なし");
                e.Handled = true; break;
            case Key.E:
                if (_path.Length > 0)
                {
                    // Office 文書 / PDF はテキストエディタでは開けないので、既定のアプリ（Word / Excel / Acrobat…）で
                    if (_docText) { try { FileOperations.ShellExecute(_path); } catch { } }
                    else FileOperations.OpenInEditor(_editor, _path);
                    Close();
                }
                e.Handled = true; break;
        }
    }
}
