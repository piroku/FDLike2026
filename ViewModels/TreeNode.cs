using System.Collections.ObjectModel;
using System.IO;

namespace FDLike2026.ViewModels;

/// <summary>ツリーのノードの種類。Folder はドライブ・フォルダ・共有フォルダ（一覧に表示できる場所）。</summary>
public enum TreeNodeKind { Folder, Network, Host }

/// <summary>左側ツリーの 1 ノード（遅延読み込み）。</summary>
public sealed class TreeNode : ObservableObject
{
    private static readonly TreeNode Dummy = new("", "(loading)", false);

    /// <summary>「ネットワーク」ノードのパス（実在のパスではない目印）。</summary>
    public const string NetworkPath = @"\\";

    /// <summary>
    /// 「ネットワーク」を開いたときに、探索を待たずに並べておくコンピューター名の供給元
    /// （JumpFolder の登録先や最近開いたフォルダなど）。ネットワークには触れないこと。
    /// </summary>
    public static Func<IEnumerable<string>>? KnownHosts { get; set; }

    public string Path { get; }
    public bool IsDrive { get; }
    public TreeNodeKind Kind { get; }

    /// <summary>一覧に表示できる場所（ドライブ・フォルダ・共有フォルダ）なら true。「ネットワーク」とコンピューターは false。</summary>
    public bool IsFileSystem => Kind == TreeNodeKind.Folder && !string.IsNullOrEmpty(Path);

    /// <summary>「(検索中...)」などの案内行（選択・移動の対象にしない）。</summary>
    public bool IsPlaceholder => string.IsNullOrEmpty(Path);

    private string _name;
    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }
    public ObservableCollection<TreeNode> Children { get; } = new();

    private System.Windows.Media.ImageSource? _icon;
    private bool _iconLoaded;
    public System.Windows.Media.ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _icon = string.IsNullOrEmpty(Path) ? null
                    : Kind == TreeNodeKind.Network ? Services.IconService.Stock(Services.IconService.SIID_MYNETWORK)
                    : Kind == TreeNodeKind.Host ? Services.IconService.Stock(Services.IconService.SIID_SERVER)
                    : IsShareRoot ? Services.IconService.Stock(Services.IconService.SIID_SERVERSHARE) ?? Services.IconService.ForFile(Path, true)
                    : Services.IconService.ForFile(Path, true);
                _iconLoaded = true;
            }
            return _icon;
        }
    }

    /// <summary>\\host\share の形（共有フォルダそのもの）。</summary>
    private bool IsShareRoot =>
        Kind == TreeNodeKind.Folder && Path.StartsWith(@"\\", StringComparison.Ordinal) &&
        Path.Trim('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries).Length == 2;

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!Set(ref _isExpanded, value)) return;
            if (value && IsUnloaded) LoadChildren();
        }
    }

    private bool IsUnloaded => Children.Count == 1 && Children[0] == Dummy;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public TreeNode(string path, string name, bool isDrive, TreeNodeKind kind = TreeNodeKind.Folder)
    {
        Path = path;
        _name = name;
        IsDrive = isDrive;
        Kind = kind;
        if (!string.IsNullOrEmpty(path)) Children.Add(Dummy);
    }

    public void LoadChildren()
    {
        if (Kind != TreeNodeKind.Folder) { LoadNetworkChildren(); return; }
        Children.Clear();
        if (Services.PathProbe.IsUnreachable(Path)) return;   // 停止中のネットワークドライブで固まらない
        try
        {
            var opt = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
            foreach (var d in new DirectoryInfo(Path).EnumerateDirectories("*", opt)
                         .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                Children.Add(new TreeNode(d.FullName, d.Name, false));
        }
        catch { }
    }

    /// <summary>
    /// 読み込み済みのノードをディスクの状態で更新する（F5・他アプリでの変更）。
    /// 開いている子はそのまま開いたまま再帰的に更新し、未展開のノードには触れない。
    /// </summary>
    public void Refresh()
    {
        if (IsUnloaded) return;   // 未展開
        if (Kind != TreeNodeKind.Folder)
        {
            // 「ネットワーク」とコンピューターは探し直さない（時間がかかるため。探し直しはツリーで F5）。開いている共有フォルダだけ更新
            foreach (var c in Children.ToList()) if (!c.IsPlaceholder) c.Refresh();
            return;
        }
        var old = Children.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        if (Services.PathProbe.IsUnreachable(Path)) return;
        List<DirectoryInfo> dirs;
        try
        {
            var opt = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
            dirs = new DirectoryInfo(Path).EnumerateDirectories("*", opt).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch { return; }

        var fresh = new List<TreeNode>();
        foreach (var d in dirs)
        {
            if (old.TryGetValue(d.Name, out var keep)) { keep.Refresh(); fresh.Add(keep); }
            else fresh.Add(new TreeNode(d.FullName, d.Name, false));
        }
        // 変化が無ければ入れ替えない（選択・スクロールを保つ）
        if (fresh.Count == Children.Count && fresh.Zip(Children).All(p => ReferenceEquals(p.First, p.Second))) return;
        Children.Clear();
        foreach (var n in fresh) Children.Add(n);
    }

    // ---- ネットワーク（コンピューター / 共有フォルダの一覧。探索は裏で行い、見つかった分を後から足す） ----

    private bool _discovering;

    private void LoadNetworkChildren()
    {
        Children.Clear();
        if (Kind == TreeNodeKind.Network)
        {
            // すぐ分かるもの（ネットワークドライブの接続先、JumpFolder の登録先など）を先に並べる
            foreach (var h in QuickHosts()) EnsureChild(@"\\" + h, h, TreeNodeKind.Host);
        }
        Discover();
    }

    /// <summary>コンピューター / 共有フォルダを探し直す（ツリーで F5）。見つかった分を足すだけで、今ある行は消さない。</summary>
    public void Discover()
    {
        if (Kind == TreeNodeKind.Folder || _discovering) return;
        if (IsUnloaded) { IsExpanded = true; return; }   // 展開すれば探索が始まる
        _discovering = true;
        SetPlaceholder("(検索中...)");
        var ui = System.Windows.Application.Current?.Dispatcher;

        if (Kind == TreeNodeKind.Network)
        {
            // 2 通りの探し方を並行して走らせ、見つかった分から足していく（全部そろうまで数十秒かかることがある）
            int pending = 2;
            void Done(Task<List<string>> t)
            {
                var hosts = t.IsCompletedSuccessfully ? t.Result : new List<string>();
                ui?.BeginInvoke(() =>
                {
                    foreach (var h in hosts) EnsureChild(@"\\" + h, h, TreeNodeKind.Host);
                    if (--pending > 0) return;
                    _discovering = false;
                    SetPlaceholder(Children.Any(c => !c.IsPlaceholder) ? null : "(コンピューターが見つかりません)");
                });
            }
            Services.NetworkBrowser.RunAsync(() => Services.NetworkBrowser.ShellHosts(
                h => ui?.BeginInvoke(() => { EnsureChild(@"\\" + h, h, TreeNodeKind.Host); }))).ContinueWith(Done);
            Services.NetworkBrowser.RunAsync(Services.NetworkBrowser.WNetHosts).ContinueWith(Done);
        }
        else
        {
            var host = Path.TrimStart('\\');
            Services.NetworkBrowser.RunAsync(() =>
            {
                if (!Services.PathProbe.IsHostReachable(host)) return (reachable: false, shares: (List<string>?)null);
                return (reachable: true, shares: Services.NetworkBrowser.EnumShares(host));
            }).ContinueWith(t =>
            {
                var r = t.IsCompletedSuccessfully ? t.Result : (reachable: true, shares: (List<string>?)null);
                ui?.BeginInvoke(() =>
                {
                    _discovering = false;
                    foreach (var s in r.shares ?? new List<string>()) EnsureChild($@"\\{host}\{s}", s, TreeNodeKind.Folder);
                    SetPlaceholder(Children.Any(c => !c.IsPlaceholder) ? null
                        : !r.reachable ? "(接続できません)"
                        : r.shares is null ? "(共有フォルダの一覧を取得できません)"
                        : "(共有フォルダがありません)");
                });
            });
        }
    }

    /// <summary>末尾の案内行を付け替える（null で消す）。</summary>
    private void SetPlaceholder(string? text)
    {
        for (int i = Children.Count - 1; i >= 0; i--)
            if (Children[i].IsPlaceholder) Children.RemoveAt(i);
        if (text is not null) Children.Add(new TreeNode("", text, false));
    }

    /// <summary>名前の一致する子を返す。無ければ名前順の位置に足す（案内行より前）。</summary>
    private TreeNode EnsureChild(string path, string name, TreeNodeKind kind)
    {
        var hit = Children.FirstOrDefault(c => !c.IsPlaceholder && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit;
        int i = 0;
        while (i < Children.Count && !Children[i].IsPlaceholder &&
               StringComparer.OrdinalIgnoreCase.Compare(Children[i].Name, name) < 0) i++;
        var node = new TreeNode(path, name, false, kind);
        Children.Insert(i, node);
        return node;
    }

    /// <summary>ネットワークに触れずに分かるコンピューター名（ネットワークドライブの接続先 ＋ KnownHosts）。</summary>
    private static IEnumerable<string> QuickHosts()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var d in Services.DriveService.QuickList())
                if (d.IsNetwork && Services.NetworkBrowser.HostFromUnc(Services.DriveService.GetUncPath(d.Letter)) is { } h) set.Add(h);
        }
        catch { }
        try
        {
            foreach (var h in KnownHosts?.Invoke() ?? Enumerable.Empty<string>())
                if (!string.IsNullOrWhiteSpace(h)) set.Add(h);
        }
        catch { }
        return set;
    }

    /// <summary>
    /// ドライブ一覧をブロックせずに作る。ボリュームラベルはバックグラウンドで後から埋める
    /// （切断されたネットワークドライブで起動が固まるのを防ぐ）。最後に「ネットワーク」を置く。
    /// </summary>
    public static ObservableCollection<TreeNode> BuildRoots()
    {
        var roots = new ObservableCollection<TreeNode>();
        foreach (var d in Services.DriveService.QuickList())
        {
            var node = new TreeNode(d.Root, $"{d.Label} ({d.Letter})", true);
            roots.Add(node);
            if (!d.IsNetwork)
            {
                var item = d;
                _ = Services.DriveService.LoadLabelAsync(item).ContinueWith(t =>
                {
                    if (t.Result is { } label)
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => node.Name = $"{label} ({item.Letter})");
                });
            }
        }
        roots.Add(new TreeNode(NetworkPath, "ネットワーク", true, TreeNodeKind.Network));
        return roots;
    }

    public static string DriveTypeName(DriveType t) => Services.DriveService.TypeName(t);

    /// <summary>指定パスまでツリーを展開して選択する。</summary>
    public static void RevealPath(IEnumerable<TreeNode> roots, string path)
    {
        var full = System.IO.Path.GetFullPath(path).TrimEnd('\\') + "\\";
        TreeNode? node;
        string[] rest;
        if (full.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // UNC: ネットワーク → コンピューター → 共有フォルダ → …（探索の結果を待たず、必要な行はその場で足す）
            var net = roots.FirstOrDefault(r => r.Kind == TreeNodeKind.Network);
            var parts = full.Trim('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (net is null || parts.Length < 2 || parts[0] is "?" or ".") return;
            net.IsExpanded = true;
            var host = net.EnsureChild(@"\\" + parts[0], parts[0], TreeNodeKind.Host);
            host.IsExpanded = true;
            node = host.EnsureChild($@"\\{parts[0]}\{parts[1]}", parts[1], TreeNodeKind.Folder);
            rest = parts.Skip(2).ToArray();
        }
        else
        {
            node = roots.FirstOrDefault(r => r.Kind == TreeNodeKind.Folder && full.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase));
            if (node is null) return;
            rest = full.Substring(node.Path.Length).Split('\\', StringSplitOptions.RemoveEmptyEntries);
        }
        node.IsExpanded = true;
        foreach (var seg in rest)
        {
            var child = node.Children.FirstOrDefault(c => string.Equals(c.Name, seg, StringComparison.OrdinalIgnoreCase));
            if (child is null) break;
            node = child;
            node.IsExpanded = true;
        }
        node.IsSelected = true;
    }
}
