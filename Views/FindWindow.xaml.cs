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

    // ---- 右クリック: Windows のメニュー / 削除 ----
    /// <summary>結果の行を右クリック: 選択行が 1 つのフォルダ内ならエクスプローラーと同じ Windows のメニュー、複数フォルダにまたがっていれば削除などの簡易メニューを出す。</summary>
    private void Results_RightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        if (ItemsControl.ContainerFromElement(Results, src) is not ListViewItem { DataContext: FoundItem f }) return;   // 見出し・余白
        e.Handled = true;
        if (!Results.SelectedItems.Contains(f)) Results.SelectedItem = f;
        ShowRowMenu(f, null);
    }

    /// <summary>アプリケーションキー / Shift+F10: 選択行の左下にメニューを出す。</summary>
    private void ShowMenuAtSelection()
    {
        if (Results.SelectedItem is not FoundItem f) return;
        ShowRowMenu(f, Results.ItemContainerGenerator.ContainerFromItem(f) is FrameworkElement { IsVisible: true } row ? row : null);
    }

    /// <summary>row が null ならマウス位置、あればその行の左下に出す。</summary>
    private void ShowRowMenu(FoundItem anchor, FrameworkElement? row)
    {
        var selected = Results.SelectedItems.Cast<FoundItem>().ToList();
        if (selected.Count == 0) return;
        bool oneFolder = selected.All(r => string.Equals(r.Folder, anchor.Folder, StringComparison.OrdinalIgnoreCase));
        if (oneFolder) { ShowShellMenu(anchor, row); return; }

        // Windows のメニューは 1 つのフォルダ内の項目にしか出せない。複数フォルダにまたがる選択には自前のメニューを出す
        int same = selected.Count(r => string.Equals(r.Folder, anchor.Folder, StringComparison.OrdinalIgnoreCase));
        var menu = new ContextMenu();
        void Add(string header, Action act)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
        }
        menu.Items.Add(new MenuItem { Header = $"選択した {selected.Count:#,0} 件（複数のフォルダ）", IsEnabled = false });
        menu.Items.Add(new Separator());
        Add("削除（ごみ箱）(_D)　　Delete", () => DeleteSelected(permanent: false));
        Add("完全に削除　　Shift+Delete", () => DeleteSelected(permanent: true));
        Add("パスをコピー(_P)　　Ctrl+C", () => Copy_Click(this, new RoutedEventArgs()));
        menu.Items.Add(new Separator());
        Add($"Windows のメニュー（この行と同じフォルダの {same:#,0} 件だけ）(_W)...", () =>
        {
            Results.SelectedItems.Clear();
            foreach (var r in selected.Where(r => string.Equals(r.Folder, anchor.Folder, StringComparison.OrdinalIgnoreCase))) Results.SelectedItems.Add(r);
            ShowShellMenu(anchor, row);
        });
        if (row is not null) { menu.PlacementTarget = row; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; }
        else menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    /// <summary>選択行（すべて anchor と同じフォルダ）の Windows のメニューを出す。</summary>
    private void ShowShellMenu(FoundItem anchor, FrameworkElement? row)
    {
        int x, y;
        if (row is not null) { var pt = row.PointToScreen(new Point(0, row.ActualHeight)); x = (int)pt.X; y = (int)pt.Y; }
        else { var mp = System.Windows.Forms.Cursor.Position; x = mp.X; y = mp.Y; }
        var targets = Results.SelectedItems.Cast<FoundItem>().ToList();
        if (targets.Count == 0) targets.Add(anchor);
        var paths = targets.Select(r => r.FullPath).ToList();
        // WPF のメニューが完全に閉じてから Win32 のメニューを出す
        Dispatcher.BeginInvoke(() =>
        {
            try { ShellContextMenu.Show(this, anchor.Folder, paths, x, y); }
            catch (Exception ex)
            {
                App.Log(ex);
                MessageBox.Show(this, "Windows のメニューを表示できませんでした:\n" + ex.Message, "ファイルの検索", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            RemoveMissing(targets);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>Delete / Shift+Delete / メニューの削除: 選択行をまとめて削除する（別々のフォルダにあってもよい）。</summary>
    private void DeleteSelected(bool permanent)
    {
        var rows = Results.SelectedItems.Cast<FoundItem>().ToList();
        if (rows.Count == 0) return;
        bool recycle = !permanent && _settings.DeleteToRecycleBin;
        bool noBin = recycle && rows.Any(r => !FileOperations.HasRecycleBin(r.FullPath));
        var how = !recycle ? "完全に削除" : noBin ? "完全に削除（ネットワーク上のためごみ箱には入りません）" : "ごみ箱へ移動";
        var what = rows.Count == 1 ? rows[0].FullPath : $"選択した {rows.Count:#,0} 件";
        var icon = recycle && !noBin ? MessageBoxImage.Question : MessageBoxImage.Warning;
        if (MessageBox.Show(this, $"{what} を{how}しますか？", "ファイルの検索", MessageBoxButton.YesNo, icon, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            // 1 件ずつ。先に親フォルダごと消えた行は FromPaths が空を返すので飛ばされる
            foreach (var r in rows) FileOperations.Delete(FileOperations.FromPaths(new[] { r.FullPath }), recycle);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.Log(ex);
            MessageBox.Show(this, "削除できませんでした:\n" + ex.Message, "ファイルの検索", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RemoveMissing(rows);
    }

    /// <summary>削除・移動されて無くなった行を一覧から消す。</summary>
    private void RemoveMissing(List<FoundItem> rows)
    {
        int index = Results.SelectedIndex;
        foreach (var r in rows)
            if (r.IsDirectory ? !Directory.Exists(r.FullPath) : !File.Exists(r.FullPath)) _results.Remove(r);
        CountText.Text = $"{_results.Count:#,0} 件";
        if (Results.SelectedItem is null && _results.Count > 0)
        {
            Results.SelectedIndex = Math.Clamp(index, 0, _results.Count - 1);
            (Results.ItemContainerGenerator.ContainerFromIndex(Results.SelectedIndex) as UIElement)?.Focus();
        }
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
            ShowMenuAtSelection(); e.Handled = true;
        }
        else if (e.Key == Key.Delete && Results.IsKeyboardFocusWithin && Results.SelectedItem is not null && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            DeleteSelected(permanent: Keyboard.Modifiers == ModifierKeys.Shift); e.Handled = true;
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e) => _cts?.Cancel();
}
