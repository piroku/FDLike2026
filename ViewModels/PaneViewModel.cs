using System.Collections.ObjectModel;
using System.IO;
using FDLike2026.Models;

namespace FDLike2026.ViewModels;

public enum SortMode { Name, Extension, Size, Date, None }

/// <summary>隠し属性のものを通常のものと分けて並べるか。</summary>
public enum HiddenGrouping
{
    Mixed,        // 区別せず名前順などで混在
    HiddenFirst,  // 隠しを先に（WinFM2008 と同じ）
    HiddenLast,   // 隠しを後に
}

/// <summary>1 つのファイル一覧ウィンドウ（左 / 右）の状態。</summary>
public sealed class PaneViewModel : ObservableObject
{
    public ObservableCollection<FileEntry> Entries { get; } = new();

    private System.Windows.GridLength _nameColWidth = new(1, System.Windows.GridUnitType.Star);
    /// <summary>名前列の幅。「文字数自動」ではこの一覧で一番長い名前に合わせる（WinFM と同じ）。</summary>
    public System.Windows.GridLength NameColWidth
    {
        get => _nameColWidth;
        private set { if (_nameColWidth != value) { _nameColWidth = value; OnPropertyChanged(); } }
    }

    // ---- git の状態列 ----
    private int _gitSeq;
    /// <summary>バックグラウンドで git status を取り、一覧の git 列へ反映する（管理外なら何もしない）。</summary>
    public void ApplyGitStatusAsync()
    {
        if (!Services.ListModeService.Current.ShowGit) return;
        var dir = Path;
        var snapshot = Entries.ToList();
        int seq = ++_gitSeq;
        Task.Run(() =>
        {
            Services.GitStatusService.Result? res;
            try { res = Services.GitStatusService.Get(dir); } catch { res = null; }
            if (res is null) return;
            var disp = System.Windows.Application.Current?.Dispatcher;
            disp?.BeginInvoke(() =>
            {
                if (seq != _gitSeq) return;   // その後に再読込されていたら捨てる
                foreach (var e in snapshot)
                {
                    if (e.IsParent) continue;
                    var (text, key) = Services.GitStatusService.For(res, e.FullPath, e.IsDirectory);
                    e.SetGit(text, key);
                }
            });
        });
    }

    /// <summary>表示モードやフォントが変わったときにも呼ぶ。</summary>
    private double _viewportWidth;
    /// <summary>一覧の表示幅（MainWindow が ListBox の SizeChanged で設定）。名前列の上限に使う。</summary>
    public double ViewportWidth
    {
        get => _viewportWidth;
        set { if (Math.Abs(_viewportWidth - value) > 1) { _viewportWidth = value; UpdateNameColumn(); } }
    }

    public void UpdateNameColumn()
    {
        var extW = Services.ListModeService.MeasureExtColumn(Entries.Where(e => !e.IsDirectory).Select(e => e.Extension));
        double maxName = _viewportWidth > 0 ? _viewportWidth - Services.ListModeService.FixedColumnsWidth() - extW.Value - 8 : 0;
        NameColWidth = Services.ListModeService.MeasureNameColumn(Entries.Select(e => e.BaseName), maxName);
        foreach (var e in Entries) { e.NameColWidth = NameColWidth; e.ExtColWidth = extW; e.RefreshDisplay(); }
    }

    private string _path = "";
    public string Path
    {
        get => _path;
        private set => Set(ref _path, value);
    }

    private string _mask = "*.*";
    public string Mask
    {
        get => _mask;
        set { if (Set(ref _mask, string.IsNullOrWhiteSpace(value) ? "*.*" : value.Trim())) Reload(); }
    }

    private SortMode _sort = SortMode.Name;
    public SortMode Sort
    {
        get => _sort;
        set { if (Set(ref _sort, value)) { OnPropertyChanged(nameof(SortText)); Reload(); } }
    }

    private bool _sortDesc;
    /// <summary>降順（WinFM の 昇順 / 降順）。</summary>
    public bool SortDescending
    {
        get => _sortDesc;
        set { if (Set(ref _sortDesc, value)) { OnPropertyChanged(nameof(SortText)); Reload(); } }
    }

    public void SetSort(SortMode mode, bool descending)
    {
        bool changed = _sort != mode || _sortDesc != descending;
        _sort = mode; _sortDesc = descending;
        OnPropertyChanged(nameof(Sort)); OnPropertyChanged(nameof(SortDescending)); OnPropertyChanged(nameof(SortText));
        if (changed) Reload();
    }

    /// <summary>情報ボックス用: 「名前 ↑」など。</summary>
    public string SortText => Sort switch
    {
        SortMode.Name => "名前", SortMode.Extension => "拡張子", SortMode.Size => "サイズ", SortMode.Date => "日付", _ => "なし",
    } + (Sort == SortMode.None ? "" : SortDescending ? " ↓" : " ↑");

    private HiddenGrouping _hiddenGrouping = HiddenGrouping.HiddenFirst;
    public HiddenGrouping HiddenGrouping
    {
        get => _hiddenGrouping;
        set { if (Set(ref _hiddenGrouping, value)) Reload(); }
    }

    private bool _showHidden = true;
    public bool ShowHidden
    {
        get => _showHidden;
        set { if (Set(ref _showHidden, value)) Reload(); }
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    private int _cursor;
    public int CursorIndex
    {
        get => _cursor;
        set
        {
            if (Entries.Count == 0) { _cursor = 0; OnPropertyChanged(); OnPropertyChanged(nameof(Current)); return; }
            value = Math.Clamp(value, 0, Entries.Count - 1);
            if (_cursor >= 0 && _cursor < Entries.Count) Entries[_cursor].IsCursor = false;
            _cursor = value;
            // 念のため: カーソル行は常に 1 つだけ（取りこぼしがあっても二重表示にならないように）
            foreach (var e in Entries) if (e.IsCursor) e.IsCursor = false;
            Entries[_cursor].IsCursor = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Current));
        }
    }

    public FileEntry? Current => Entries.Count > 0 && _cursor >= 0 && _cursor < Entries.Count ? Entries[_cursor] : null;

    // ボリューム情報
    public long FreeBytes { get; private set; }
    public long TotalBytes { get; private set; }
    public int FileCount { get; private set; }
    public int DirCount { get; private set; }
    public long ListedBytes { get; private set; }
    public int TagCount => Entries.Count(e => e.Tagged);
    public long TagBytes => Entries.Where(e => e.Tagged).Sum(e => e.Size);
    public string? LastError { get; private set; }

    /// <summary>親に戻ったときに元の子ディレクトリへカーソルを合わせるための記憶。</summary>
    private readonly Dictionary<string, string> _cursorMemory = new(StringComparer.OrdinalIgnoreCase);

    // ---- 他アプリ（エクスプローラー等）による更新の自動反映 ----------------------
    private FileSystemWatcher? _watcher;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(400),
    };
    private bool _timerHooked;

    /// <summary>他アプリの変更を検知して自動で再読込したとき（ツリーの更新などに使う）。</summary>
    public event Action<PaneViewModel>? AutoRefreshed;

    private void StartWatcher()
    {
        StopWatcher();
        if (!_timerHooked)
        {
            _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); Services.GitStatusService.Invalidate(); Reload(); AutoRefreshed?.Invoke(this); };
            _timerHooked = true;
        }
        if (Services.PathProbe.IsUnreachable(Path)) { _watcher = null; return; }
        try
        {
            var w = new FileSystemWatcher(Path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size |
                               NotifyFilters.LastWrite | NotifyFilters.Attributes,
                IncludeSubdirectories = false,
                InternalBufferSize = 64 * 1024,
            };
            FileSystemEventHandler h = (_, _) => RequestRefresh();
            w.Created += h;
            w.Deleted += h;
            w.Changed += h;
            w.Renamed += (_, _) => RequestRefresh();
            w.Error += (_, _) => RequestRefresh();   // バッファ溢れ時も再読込で追従
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch
        {
            _watcher = null;   // ネットワークドライブ等で監視できない場合は F5 / ウィンドウ再アクティブ時に更新
        }
    }

    private void StopWatcher()
    {
        if (_watcher is null) return;
        try { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); } catch { }
        _watcher = null;
    }

    /// <summary>連続イベントをまとめ、少し置いてから UI スレッドで再読込する。</summary>
    public void RequestRefresh()
    {
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp is null) return;
        disp.BeginInvoke(() => { _refreshTimer.Stop(); _refreshTimer.Start(); });
    }

    // ---- 履歴（Alt+← / Alt+→、Ctrl+H） ----
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    /// <summary>最近開いたフォルダ（左右共通・新しい順・最大 30）。</summary>
    public static readonly List<string> Recent = new();

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    public void GoBack()
    {
        if (_back.Count == 0) return;
        var target = _back.Pop();
        _forward.Push(Path);
        Load(target, null, record: false);
    }

    public void GoForward()
    {
        if (_forward.Count == 0) return;
        var target = _forward.Pop();
        _back.Push(Path);
        Load(target, null, record: false);
    }

    public void Load(string path, string? selectName = null) => Load(path, selectName, record: true);

    private void Load(string path, string? selectName, bool record)
    {
        path = System.IO.Path.GetFullPath(path);
        if (Services.PathProbe.IsUnreachable(path))
        {
            LastError = $"接続できません（相手先が停止中または到達不能）: {path}";
            OnPropertyChanged(nameof(LastError));
            return;
        }
        if (!Services.PathProbe.DirectoryExists(path))
        {
            LastError = $"ディレクトリが見つかりません: {path}";
            OnPropertyChanged(nameof(LastError));
            return;
        }
        if (!string.IsNullOrEmpty(Path) && Current is not null && !Current.IsParent)
            _cursorMemory[Path] = Current.Name;

        if (record && !string.IsNullOrEmpty(Path) && !string.Equals(Path, path, StringComparison.OrdinalIgnoreCase))
        {
            _back.Push(Path);
            _forward.Clear();
        }
        Recent.RemoveAll(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, path);
        if (Recent.Count > 30) Recent.RemoveRange(30, Recent.Count - 30);

        Path = path;
        LastError = null;
        Reload(selectName);
        StartWatcher();
    }

    /// <summary>Ctrl+K: ワイルドカードに一致するファイルにタグを付ける（ディレクトリは対象外）。</summary>
    public int TagByWildcard(string pattern, bool tag = true)
    {
        var parts = pattern.Split(new[] { ' ', '　', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return 0;
        var regexes = parts.Select(p => new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
        int n = 0;
        foreach (var e in Entries)
        {
            if (e.IsParent || e.IsDirectory) continue;
            if (regexes.Any(r => r.IsMatch(e.Name))) { e.Tagged = tag; n++; }
        }
        NotifyCounts();
        return n;
    }

    /// <summary>Home: ファイル全部にタグが付いていれば全解除、そうでなければ全部に付ける（WinFM と同じトグル）。</summary>
    public void ToggleTagAllFiles(bool includeDirs)
    {
        var targets = Entries.Where(e => !e.IsParent && (includeDirs || !e.IsDirectory)).ToList();
        bool all = targets.Count > 0 && targets.All(e => e.Tagged);
        foreach (var e in targets) e.Tagged = !all;
        NotifyCounts();
    }

    public void Reload(string? selectName = null)
    {
        if (string.IsNullOrEmpty(Path)) return;
        var keep = selectName ?? Current?.Name;
        if (keep is null && _cursorMemory.TryGetValue(Path, out var mem)) keep = mem;

        var tagged = Entries.Where(e => e.Tagged).Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Entries) e.IsCursor = false;
        Entries.Clear();

        var list = new List<FileEntry>();
        try
        {
            var di = new DirectoryInfo(Path);
            var opt = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
            foreach (var d in di.EnumerateDirectories("*", opt))
            {
                if (!ShowHidden && d.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                list.Add(new FileEntry(d));
            }
            foreach (var f in di.EnumerateFiles(Mask, opt))
            {
                if (!ShowHidden && f.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                list.Add(new FileEntry(f));
            }
            var root = di.Root.FullName;
            if (Services.PathProbe.IsNetworkPath(root))
            {
                // ネットワークドライブの空き容量は同期で問い合わせると固まることがあるので裏で取る
                var path = Path;
                _ = Task.Run(() =>
                {
                    try
                    {
                        var nd = new DriveInfo(root);
                        long free = nd.IsReady ? nd.AvailableFreeSpace : 0, total = nd.IsReady ? nd.TotalSize : 0;
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            if (!string.Equals(Path, path, StringComparison.OrdinalIgnoreCase)) return;
                            FreeBytes = free; TotalBytes = total;
                            OnPropertyChanged(nameof(FreeBytes)); OnPropertyChanged(nameof(TotalBytes));
                        });
                    }
                    catch { }
                });
            }
            else
            {
                var drive = new DriveInfo(root);
                FreeBytes = drive.IsReady ? drive.AvailableFreeSpace : 0;
                TotalBytes = drive.IsReady ? drive.TotalSize : 0;
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        IEnumerable<FileEntry> dirs = list.Where(e => e.IsDirectory);
        IEnumerable<FileEntry> files = list.Where(e => !e.IsDirectory);
        // 隠し属性（＋システム属性）のものをグループ分けするキー。Mixed なら全部 0。
        int HiddenKey(FileEntry e) => HiddenGrouping switch
        {
            HiddenGrouping.HiddenFirst => e.IsHidden || e.IsSystem ? 0 : 1,
            HiddenGrouping.HiddenLast => e.IsHidden || e.IsSystem ? 1 : 0,
            _ => 0,
        };
        // 昇順 = 名前 A→Z / 拡張子 A→Z / 日付 古→新 / サイズ 小→大。降順はその逆（WinFM の 昇順 / 降順）
        var cmp = StringComparer.OrdinalIgnoreCase;
        IOrderedEnumerable<FileEntry> Apply(IOrderedEnumerable<FileEntry> g, bool isDir)
        {
            bool d = SortDescending;
            IOrderedEnumerable<FileEntry> By<T>(Func<FileEntry, T> key) => d ? g.ThenByDescending(key) : g.ThenBy(key);
            IOrderedEnumerable<FileEntry> ByS(Func<FileEntry, string> key) => d ? g.ThenByDescending(key, cmp) : g.ThenBy(key, cmp);
            return Sort switch
            {
                SortMode.Name => ByS(e => e.Name),
                SortMode.Extension => isDir ? ByS(e => e.Name) : ByS(e => e.Extension).ThenBy(e => e.Name, cmp),
                SortMode.Size => isDir ? g.ThenBy(e => e.Name, cmp) : By(e => e.Size).ThenBy(e => e.Name, cmp),
                SortMode.Date => By(e => e.Stamp).ThenBy(e => e.Name, cmp),
                _ => g,
            };
        }
        dirs = Apply(dirs.OrderBy(HiddenKey), isDir: true);
        files = Apply(files.OrderBy(HiddenKey), isDir: false);

        var parent = Directory.GetParent(Path);
        if (parent is not null) Entries.Add(FileEntry.CreateParent(parent.FullName));
        foreach (var e in dirs) Entries.Add(e);
        foreach (var e in files) Entries.Add(e);

        DirCount = Entries.Count(e => e.IsDirectory && !e.IsParent);
        FileCount = Entries.Count(e => !e.IsDirectory);
        ListedBytes = Entries.Sum(e => e.Size);
        foreach (var e in Entries) if (tagged.Contains(e.Name)) e.Tagged = true;
        UpdateNameColumn();
        ApplyGitStatusAsync();

        var idx = keep is null ? -1 : IndexOfName(keep);
        _cursor = -1;
        CursorIndex = idx >= 0 ? idx : 0;
        NotifyCounts();
    }

    public int IndexOfName(string name)
    {
        for (int i = 0; i < Entries.Count; i++)
            if (string.Equals(Entries[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public void NotifyCounts()
    {
        OnPropertyChanged(nameof(FreeBytes));
        OnPropertyChanged(nameof(TotalBytes));
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(DirCount));
        OnPropertyChanged(nameof(ListedBytes));
        OnPropertyChanged(nameof(TagCount));
        OnPropertyChanged(nameof(TagBytes));
        OnPropertyChanged(nameof(LastError));
        OnPropertyChanged(nameof(Current));
    }

    // ---- カーソル / タグ操作 -------------------------------------------------

    public void MoveCursor(int delta) => CursorIndex = CursorIndex + delta;
    public void CursorHome() => CursorIndex = 0;
    public void CursorEnd() => CursorIndex = Entries.Count - 1;

    public void ToggleTagAndAdvance()
    {
        var cur = Current;
        if (cur is null) return;
        if (!cur.IsParent) cur.Tagged = !cur.Tagged;
        MoveCursor(1);
        NotifyCounts();
    }

    /// <summary>カーソル行から指定行までの範囲にタグを付ける（Shift+クリック。WinFM と同じく範囲内はすべてタグ ON）。カーソルは指定行へ。</summary>
    public void TagRangeTo(int index)
    {
        if (index < 0 || index >= Entries.Count) return;
        int a = Math.Min(CursorIndex, index), b = Math.Max(CursorIndex, index);
        for (int i = a; i <= b; i++) if (!Entries[i].IsParent) Entries[i].Tagged = true;
        CursorIndex = index;
        NotifyCounts();
    }

    /// <summary>指定行のタグを反転する（Ctrl+クリック）。カーソルは指定行へ。</summary>
    public void ToggleTagAt(int index)
    {
        if (index < 0 || index >= Entries.Count) return;
        if (!Entries[index].IsParent) Entries[index].Tagged = !Entries[index].Tagged;
        CursorIndex = index;
        NotifyCounts();
    }

    public void TagAll(bool tag)
    {
        foreach (var e in Entries) if (!e.IsParent) e.Tagged = tag;
        NotifyCounts();
    }

    public void TagFilesOnly()
    {
        foreach (var e in Entries) if (!e.IsParent) e.Tagged = !e.IsDirectory;
        NotifyCounts();
    }

    public void ReverseTags()
    {
        foreach (var e in Entries) if (!e.IsParent) e.Tagged = !e.Tagged;
        NotifyCounts();
    }

    /// <summary>タグが付いていればタグ対象、無ければカーソル位置 1 件（".." は除く）。</summary>
    public IReadOnlyList<FileEntry> TargetEntries()
    {
        var tagged = Entries.Where(e => e.Tagged).ToList();
        if (tagged.Count > 0) return tagged;
        return Current is { IsParent: false } c ? new[] { c } : Array.Empty<FileEntry>();
    }

    /// <summary>先頭文字でジャンプ（WinFM のインクリメンタルサーチ簡易版）。</summary>
    public void JumpTo(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return;
        int n = Entries.Count;
        for (int k = 1; k <= n; k++)
        {
            int i = (CursorIndex + k) % n;
            if (Entries[i].Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { CursorIndex = i; return; }
        }
    }
}
