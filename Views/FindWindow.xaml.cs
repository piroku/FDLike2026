using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FDLike2026.Services;

namespace FDLike2026.Views;

public sealed record FoundItem(string FullPath, string Name, string Folder, bool IsDirectory, long Size, DateTime LastWrite)
{
    public string SizeText => IsDirectory ? "< DIR >" : Size.ToString("#,0");
    public string DateText => LastWrite.ToString("yyyy/MM/dd HH:mm");
}

/// <summary>
/// F: ファイルの検索（WinFM2008 の「ファイルの検索」相当）。
/// 名前のワイルドカードで、指定フォルダ（サブフォルダ含む）を非同期に走査する。
/// </summary>
public partial class FindWindow : Window
{
    private readonly Settings _settings;
    private readonly string _currentDir;
    private readonly ObservableCollection<FoundItem> _results = new();
    private CancellationTokenSource? _cts;

    /// <summary>「移動」で選ばれたファイル / フォルダのフルパス。閉じただけなら null。</summary>
    public string? SelectedPath { get; private set; }

    /// <summary>検索語の履歴（セッション内）。</summary>
    private static readonly List<string> History = new();

    public FindWindow(Window owner, Settings settings, string currentDir)
    {
        InitializeComponent();
        Owner = owner;
        _settings = settings;
        _currentDir = currentDir;
        PathBox.Text = currentDir;
        NameBox.ItemsSource = History;
        Results.ItemsSource = _results;
        Loaded += (_, _) => { NameBox.Focus(); };
    }

    // ---- 探す場所 ----
    private void Current_Click(object sender, RoutedEventArgs e) => PathBox.Text = _currentDir;
    private void Root_Click(object sender, RoutedEventArgs e) => PathBox.Text = Path.GetPathRoot(PathBox.Text) ?? _currentDir;

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
    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Start_Click(sender, e); e.Handled = true; }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null) return;
        var root = PathBox.Text.Trim().Trim('"');
        if (!Directory.Exists(root))
        {
            MessageBox.Show(this, $"探す場所が見つかりません:\n{root}", "ファイルの検索", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var pattern = (NameBox.Text ?? "").Trim();
        if (pattern.Length > 0 && !History.Contains(pattern)) { History.Insert(0, pattern); NameBox.ItemsSource = null; NameBox.ItemsSource = History; NameBox.Text = pattern; }
        var regexes = BuildRegexes(pattern);
        bool sub = SubCheck.IsChecked == true, dirs = DirCheck.IsChecked == true, hidden = HiddenCheck.IsChecked == true;

        _results.Clear();
        CountText.Text = "";
        StartButton.IsEnabled = false; StopButton.IsEnabled = true; CopyButton.IsEnabled = GoButton.IsEnabled = false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<string>(dir => ScanText.Text = dir);
        var found = new Progress<List<FoundItem>>(batch => { foreach (var f in batch) _results.Add(f); CountText.Text = $"{_results.Count:#,0} 件"; });

        try
        {
            await Task.Run(() => Scan(root, regexes, sub, dirs, hidden, progress, found, token), token);
            ScanText.Text = token.IsCancellationRequested ? "（中断しました）" : "（検索完了）";
        }
        catch (OperationCanceledException) { ScanText.Text = "（中断しました）"; }
        catch (Exception ex) { ScanText.Text = "エラー: " + ex.Message; }
        finally
        {
            CountText.Text = $"{_results.Count:#,0} 件";
            StartButton.IsEnabled = true; StopButton.IsEnabled = false; CopyButton.IsEnabled = _results.Count > 0;
            _cts?.Dispose(); _cts = null;
            if (_results.Count > 0 && Results.SelectedIndex < 0) Results.SelectedIndex = 0;
        }
    }

    /// <summary>スペース区切りの複数ワイルドカードを正規表現にする。ワイルドカードが無ければ部分一致。</summary>
    private static List<Regex> BuildRegexes(string pattern)
    {
        var list = new List<Regex>();
        foreach (var raw in pattern.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = raw;
            if (!p.Contains('*') && !p.Contains('?')) p = "*" + p + "*";
            var rx = "^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            list.Add(new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }
        return list;
    }

    private static void Scan(string root, List<Regex> regexes, bool sub, bool includeDirs, bool hidden,
                             IProgress<string> progress, IProgress<List<FoundItem>> found, CancellationToken token)
    {
        var batch = new List<FoundItem>();
        var lastReport = DateTime.MinValue;
        var stack = new Stack<string>();
        stack.Push(root);
        var opt = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = hidden ? 0 : FileAttributes.Hidden | FileAttributes.System };

        bool Match(string name) => regexes.Count == 0 || regexes.Any(r => r.IsMatch(name));

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
                bool isDir = e is DirectoryInfo;
                if (isDir)
                {
                    // 再解析ポイント（ジャンクション等）は無限ループ防止で下りない
                    if (sub && !e.Attributes.HasFlag(FileAttributes.ReparsePoint)) stack.Push(e.FullName);
                    if (!includeDirs) continue;
                }
                if (!Match(e.Name)) continue;
                long size = e is FileInfo fi ? fi.Length : 0;
                batch.Add(new FoundItem(e.FullName, e.Name, Path.GetDirectoryName(e.FullName) ?? "", isDir, size, e.LastWriteTime));
                if (batch.Count >= 50) { found.Report(new List<FoundItem>(batch)); batch.Clear(); }
            }
        }
        if (batch.Count > 0) found.Report(new List<FoundItem>(batch));
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // ---- 結果 ----
    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
        { GoButton.IsEnabled = Results.SelectedItem is not null; CopyButton.IsEnabled = _results.Count > 0; }

    private void Results_DoubleClick(object sender, MouseButtonEventArgs e) => Go_Click(sender, e);

    private void Go_Click(object sender, RoutedEventArgs e)
    {
        if (Results.SelectedItem is not FoundItem f) return;
        SelectedPath = f.FullPath;
        _cts?.Cancel();
        Close();
    }

    /// <summary>Ctrl+C / コピー(P): 見つかったファイルのフルパスを 1 行 1 件でクリップボードへ（複数選択していればその分だけ）。</summary>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var rows = Results.SelectedItems.Count > 1 ? Results.SelectedItems.Cast<FoundItem>().ToList() : _results.ToList();
        if (rows.Count == 0) return;
        try { Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(r => r.FullPath)) + Environment.NewLine); } catch { }
    }

    // ---- 右クリック: Windows のメニュー ----
    /// <summary>結果の行を右クリック: エクスプローラーと同じ Windows のメニューを出す（複数選択していればその分が対象）。</summary>
    private void Results_RightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        if (ItemsControl.ContainerFromElement(Results, src) is not ListViewItem { DataContext: FoundItem f }) return;   // 見出し・余白
        e.Handled = true;
        if (!Results.SelectedItems.Contains(f)) Results.SelectedItem = f;
        var mp = System.Windows.Forms.Cursor.Position;
        ShowShellMenu(f, mp.X, mp.Y);
    }

    /// <summary>アプリケーションキー / Shift+F10: 選択行の左下にメニューを出す。</summary>
    private void ShowShellMenuAtSelection()
    {
        if (Results.SelectedItem is not FoundItem f) return;
        int x, y;
        if (Results.ItemContainerGenerator.ContainerFromItem(f) is FrameworkElement row && row.IsVisible)
        {
            var pt = row.PointToScreen(new Point(0, row.ActualHeight));
            x = (int)pt.X; y = (int)pt.Y;
        }
        else { var mp = System.Windows.Forms.Cursor.Position; x = mp.X; y = mp.Y; }
        ShowShellMenu(f, x, y);
    }

    private void ShowShellMenu(FoundItem anchor, int x, int y)
    {
        // Windows のメニューは 1 つのフォルダ内の項目にしか出せないので、別フォルダの行は選択から外す（対象が見た目どおりになるように）
        var selected = Results.SelectedItems.Cast<FoundItem>().ToList();
        var targets = selected.Where(r => string.Equals(r.Folder, anchor.Folder, StringComparison.OrdinalIgnoreCase)).ToList();
        if (targets.Count == 0) targets.Add(anchor);
        if (targets.Count != selected.Count)
        {
            Results.SelectedItems.Clear();
            foreach (var r in targets) Results.SelectedItems.Add(r);
            ScanText.Text = "（Windows のメニューは同じフォルダ内の行が対象です。別フォルダの行は選択から外しました）";
        }
        var paths = targets.Select(r => r.FullPath).ToList();
        Dispatcher.BeginInvoke(() =>
        {
            try { ShellContextMenu.Show(this, anchor.Folder, paths, x, y); }
            catch (Exception ex)
            {
                App.Log(ex);
                MessageBox.Show(this, "Windows のメニューを表示できませんでした:\n" + ex.Message, "ファイルの検索", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // メニューから削除・移動された行は一覧から消す
            foreach (var r in targets)
                if (r.IsDirectory ? !Directory.Exists(r.FullPath) : !File.Exists(r.FullPath)) _results.Remove(r);
            CountText.Text = $"{_results.Count:#,0} 件";
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Close_Click(object sender, RoutedEventArgs e) { _cts?.Cancel(); Close(); }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (AccessKeys.TryHandle(this, e)) { e.Handled = true; return; }   // (_X) の X キー（Alt なし）
        if (e.Key == Key.Escape)
        {
            if (_cts is not null) { _cts.Cancel(); e.Handled = true; return; }   // 検索中は Esc で中断、もう一度で閉じる
            Close(); e.Handled = true;
        }
        else if (e.Key == Key.Enter && Results.IsKeyboardFocusWithin && Results.SelectedItem is not null)
        {
            Go_Click(sender, e); e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && Results.IsKeyboardFocusWithin)
        {
            Copy_Click(sender, e); e.Handled = true;
        }
        else if (Results.IsKeyboardFocusWithin && (e.Key == Key.Apps || (Keyboard.Modifiers == ModifierKeys.Shift && (e.Key == Key.F10 || (e.Key == Key.System && e.SystemKey == Key.F10)))))
        {
            ShowShellMenuAtSelection(); e.Handled = true;
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e) => _cts?.Cancel();
}
