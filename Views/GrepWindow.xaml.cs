using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FDLike2026.Services;

namespace FDLike2026.Views;

/// <summary>grep の 1 ヒット（1 行）。内容は一致部分の前 / 一致 / 後ろに分けて持つ（強調表示用）。</summary>
public sealed record GrepHit(string FullPath, string Name, string Folder, int Line, string Pre, string Hit, string Post)
{
    public string LineText => Pre + Hit + Post;
}

/// <summary>
/// Shift+F: 文字列の検索（grep）。指定フォルダ配下のテキストファイルの中身を検索し、ヒット行を一覧にする。
/// 結果から「移動」「ビューアで該当行」「エディタで該当行」「ヒットした全ファイルにタグ」ができる。
/// </summary>
public partial class GrepWindow : Window
{
    private readonly Settings _settings;
    private readonly string _currentDir;
    private readonly ObservableCollection<GrepHit> _results = new();
    private CancellationTokenSource? _cts;
    private int _fileCount;

    /// <summary>「移動」で選ばれたファイルのフルパス。</summary>
    public string? SelectedPath { get; private set; }
    /// <summary>「タグ付け」で選ばれたファイル群（フォルダごと）。</summary>
    public List<string>? TagPaths { get; private set; }

    public GrepWindow(Window owner, Settings settings, string currentDir)
    {
        InitializeComponent();
        Owner = owner;
        _settings = settings;
        _currentDir = currentDir;
        PathBox.Text = currentDir;
        TextBox1.ItemsSource = settings.GrepHistory;
        if (settings.GrepHistory.Count > 0) TextBox1.Text = settings.GrepHistory[0];
        MaskBox.Text = settings.GrepFileMask;
        DocCheck.IsChecked = settings.GrepDocuments;
        RegexCheck.IsChecked = settings.GrepRegex;
        CaseCheck.IsChecked = settings.GrepCaseSensitive;
        WordCheck.IsChecked = settings.GrepWholeWord;
        MaxMbBox.Text = settings.GrepMaxFileMB.ToString();
        Results.ItemsSource = _results;
        Loaded += (_, _) =>
        {
            TextBox1.Focus();
            if (TextBox1.Template.FindName("PART_EditableTextBox", TextBox1) is TextBox tb) tb.SelectAll();
        };
    }

    // ---- 探す場所 ----
    private void Current_Click(object sender, RoutedEventArgs e) => PathBox.Text = _currentDir;
    private void Root_Click(object sender, RoutedEventArgs e) => PathBox.Text = Path.GetPathRoot(PathBox.Text) ?? _currentDir;
    private void AllFiles_Click(object sender, RoutedEventArgs e) => MaskBox.Text = "";

    private void Jump_Click(object sender, RoutedEventArgs e)
    {
        var w = new JumpWindow(this, _settings, PathBox.Text);
        w.ShowDialog();
        if (w.SelectedPath is { } p) PathBox.Text = p;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "探す場所",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(PathBox.Text) ? PathBox.Text : _currentDir,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) PathBox.Text = dlg.SelectedPath;
    }

    // ---- 検索 ----
    private void TextBox1_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Start_Click(sender, e); e.Handled = true; }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null) return;
        var root = PathBox.Text.Trim().Trim('"');
        if (!Directory.Exists(root))
        {
            MessageBox.Show(this, $"探す場所が見つかりません:\n{root}", "文字列の検索", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var text = (TextBox1.Text ?? "");
        if (text.Length == 0) { TextBox1.Focus(); return; }

        bool regex = RegexCheck.IsChecked == true, cs = CaseCheck.IsChecked == true, word = WordCheck.IsChecked == true;
        Regex rx;
        try
        {
            var pat = regex ? text : Regex.Escape(text);
            if (word) pat = $@"(?<![\p{{L}}\p{{N}}_]){pat}(?![\p{{L}}\p{{N}}_])";
            rx = new Regex(pat, (cs ? 0 : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(this, "正規表現が正しくありません:\n" + ex.Message, "文字列の検索", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 設定・履歴を保存
        _settings.GrepHistory.RemoveAll(h => string.Equals(h, text, StringComparison.Ordinal));
        _settings.GrepHistory.Insert(0, text);
        if (_settings.GrepHistory.Count > 30) _settings.GrepHistory.RemoveRange(30, _settings.GrepHistory.Count - 30);
        TextBox1.ItemsSource = null; TextBox1.ItemsSource = _settings.GrepHistory; TextBox1.Text = text;
        _settings.GrepFileMask = MaskBox.Text.Trim();
        _settings.GrepRegex = regex; _settings.GrepCaseSensitive = cs; _settings.GrepWholeWord = word;
        _settings.GrepDocuments = DocCheck.IsChecked == true;
        if (int.TryParse(MaxMbBox.Text, out var mb)) _settings.GrepMaxFileMB = Math.Clamp(mb, 1, 2000);
        _settings.Save();

        var masks = BuildMasks(MaskBox.Text);
        bool sub = SubCheck.IsChecked == true, hidden = HiddenCheck.IsChecked == true, docs = DocCheck.IsChecked == true;
        long maxBytes = (long)_settings.GrepMaxFileMB * 1024 * 1024;

        _results.Clear();
        _fileCount = 0;
        CountText.Text = "";
        StartButton.IsEnabled = false; StopButton.IsEnabled = true;
        UpdateButtons();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<string>(dir => ScanText.Text = dir);
        var found = new Progress<(List<GrepHit> hits, int files)>(r =>
        {
            foreach (var h in r.hits) _results.Add(h);
            _fileCount = r.files;
            CountText.Text = $"{_results.Count:#,0} 行 / {_fileCount:#,0} ファイル";
            UpdateButtons();
        });

        try
        {
            await Task.Run(() => Scan(root, rx, masks, sub, hidden, docs, maxBytes, progress, found, token), token);
            ScanText.Text = token.IsCancellationRequested ? "（中断しました）" : "（検索完了）";
        }
        catch (OperationCanceledException) { ScanText.Text = "（中断しました）"; }
        catch (Exception ex) { ScanText.Text = "エラー: " + ex.Message; }
        finally
        {
            CountText.Text = $"{_results.Count:#,0} 行 / {_fileCount:#,0} ファイル";
            StartButton.IsEnabled = true; StopButton.IsEnabled = false;
            _cts?.Dispose(); _cts = null;
            if (_results.Count > 0 && Results.SelectedIndex < 0) { Results.SelectedIndex = 0; Results.Focus(); }
            UpdateButtons();
        }
    }

    private static List<Regex> BuildMasks(string pattern)
    {
        var list = new List<Regex>();
        foreach (var raw in pattern.Split(new[] { ' ', '　', ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var rx = "^" + Regex.Escape(raw).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            list.Add(new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }
        return list;
    }

    private static void Scan(string root, Regex rx, List<Regex> masks, bool sub, bool hidden, bool docs, long maxBytes,
                             IProgress<string> progress, IProgress<(List<GrepHit>, int)> found, CancellationToken token)
    {
        var batch = new List<GrepHit>();
        int files = 0;
        var lastReport = DateTime.MinValue;
        var stack = new Stack<string>();
        stack.Push(root);
        var opt = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = hidden ? 0 : FileAttributes.Hidden | FileAttributes.System };
        var buf = new byte[0];

        void Flush() { if (batch.Count > 0) { found.Report((new List<GrepHit>(batch), files)); batch.Clear(); } }

        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            if ((DateTime.Now - lastReport).TotalMilliseconds > 80) { progress.Report(dir); lastReport = DateTime.Now; }

            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", opt); }
            catch { continue; }

            foreach (var e in entries)
            {
                token.ThrowIfCancellationRequested();
                if (e is DirectoryInfo)
                {
                    if (sub && !e.Attributes.HasFlag(FileAttributes.ReparsePoint)) stack.Push(e.FullName);
                    continue;
                }
                if (e is not FileInfo fi) continue;
                bool isDoc = docs && DocumentText.IsDocument(fi.Extension);
                if (!isDoc && masks.Count > 0 && !masks.Any(m => m.IsMatch(fi.Name))) continue;
                if (fi.Length == 0 || fi.Length > maxBytes) continue;

                string text;
                if (isDoc)
                {
                    // Office 文書 / PDF: 中身のテキストを取り出して、それを grep する
                    if ((DateTime.Now - lastReport).TotalMilliseconds > 80) { progress.Report(fi.FullName); lastReport = DateTime.Now; }
                    var extracted = DocumentText.Extract(fi.FullName);
                    if (extracted is null) continue;
                    text = extracted;
                }
                else
                try
                {
                    int len = (int)fi.Length;
                    if (buf.Length < len) buf = new byte[Math.Max(len, buf.Length * 2)];
                    using var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
                    int read = 0; while (read < len) { int n = fs.Read(buf, read, len - read); if (n <= 0) break; read += n; }
                    if (TextDecoder.LooksBinary(buf, read)) continue;
                    text = TextDecoder.Decode(buf, read);
                }
                catch { continue; }

                bool hitInFile = false;
                int lineNo = 0, pos = 0;
                while (pos <= text.Length)
                {
                    int nl = text.IndexOf('\n', pos);
                    int end = nl < 0 ? text.Length : nl;
                    lineNo++;
                    var line = text.AsSpan(pos, end - pos).TrimEnd('\r').ToString();
                    Match m;
                    try { m = rx.Match(line); } catch (RegexMatchTimeoutException) { m = Match.Empty; }
                    if (m.Success)
                    {
                        hitInFile = true;
                        // 長い行は一致部分の前後だけ残す
                        int pre0 = Math.Max(0, m.Index - 60);
                        string pre = (pre0 > 0 ? "…" : "") + line[pre0..m.Index];
                        int postEnd = Math.Min(line.Length, m.Index + m.Length + 160);
                        string post = line[(m.Index + m.Length)..postEnd] + (postEnd < line.Length ? "…" : "");
                        batch.Add(new GrepHit(fi.FullName, fi.Name, Path.GetDirectoryName(fi.FullName) ?? "", lineNo, pre, m.Value, post));
                        if (batch.Count >= 100) Flush();
                    }
                    if (nl < 0) break;
                    pos = nl + 1;
                }
                if (hitInFile) { files++; if (batch.Count == 0) found.Report((new List<GrepHit>(), files)); }
            }
        }
        Flush();
        found.Report((new List<GrepHit>(), files));
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // ---- 結果 ----
    private GrepHit? Current => Results.SelectedItem as GrepHit;

    private void UpdateButtons()
    {
        bool any = Current is not null;
        GoButton.IsEnabled = ViewButton.IsEnabled = EditButton.IsEnabled = any;
        CopyButton.IsEnabled = TagButton.IsEnabled = _results.Count > 0;
    }

    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();
    private void Results_DoubleClick(object sender, MouseButtonEventArgs e) => View_Click(sender, e);

    private void Go_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } h) return;
        SelectedPath = h.FullPath;
        _cts?.Cancel();
        Close();
    }

    private void View_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } h) return;
        try { new ViewerWindow(this, h.FullPath, _settings.EditorPath, h.Line, TextBox1.Text, RegexCheck.IsChecked == true, CaseCheck.IsChecked == true).ShowDialog(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "ビューア", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } h) return;
        try
        {
            if (DocumentText.IsDocument(Path.GetExtension(h.FullPath))) FileOperations.ShellExecute(h.FullPath);   // Word / Excel / PDF は既定のアプリで
            else FileOperations.OpenInEditorAtLine(_settings.EditorPath, _settings.EditorLineArgs, h.FullPath, h.Line);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "エディタ", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Tag_Click(object sender, RoutedEventArgs e)
    {
        if (_results.Count == 0) return;
        TagPaths = _results.Select(r => r.FullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _cts?.Cancel();
        Close();
    }

    /// <summary>Ctrl+C / コピー(P): 結果の一覧をテキストでクリップボードへ（複数選択していればその行だけ）。「パス(行): 内容」の grep 風にも読める タブ区切り。</summary>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var rows = Results.SelectedItems.Count > 1 ? Results.SelectedItems.Cast<GrepHit>().ToList() : _results.ToList();
        if (rows.Count == 0) return;
        var sb = new System.Text.StringBuilder();
        foreach (var h in rows) sb.Append(h.FullPath).Append('\t').Append(h.Line).Append('\t').Append(h.LineText.Replace('\t', ' ')).Append(Environment.NewLine);
        try { FileOperations.SetClipboardText(sb.ToString()); ScanText.Text = $"（{rows.Count} 行をクリップボードへコピーしました）"; } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) { _cts?.Cancel(); Close(); }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_cts is not null) { _cts.Cancel(); e.Handled = true; return; }
            Close(); e.Handled = true;
            return;
        }
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && !IsTypingInBox()) { Copy_Click(sender, e); e.Handled = true; return; }
        // Results list keys first: E = editor (not the RegexCheck access key E), M / V / T
        if (Results.IsKeyboardFocusWithin && Current is not null)
        {
            var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            switch (key)
            {
                case Key.Enter: case Key.M: Go_Click(sender, e); e.Handled = true; return;
                case Key.V: View_Click(sender, e); e.Handled = true; return;
                case Key.E: Edit_Click(sender, e); e.Handled = true; return;
                case Key.T: Tag_Click(sender, e); e.Handled = true; return;
            }
        }
        if (AccessKeys.TryHandle(this, e)) { e.Handled = true; return; }   // (_X) の X キー（Alt なし）
    }

    private static bool IsTypingInBox()
    {
        var f = Keyboard.FocusedElement as DependencyObject;
        while (f is not null)
        {
            if (f is System.Windows.Controls.Primitives.TextBoxBase || f is ComboBox { IsEditable: true }) return true;
            f = System.Windows.Media.VisualTreeHelper.GetParent(f);
        }
        return false;
    }

    private void Window_Closing(object sender, CancelEventArgs e) => _cts?.Cancel();
}
