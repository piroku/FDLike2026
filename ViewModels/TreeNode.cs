using System.Collections.ObjectModel;
using System.IO;

namespace FDLike2026.ViewModels;

/// <summary>左側ツリーの 1 ノード（遅延読み込み）。</summary>
public sealed class TreeNode : ObservableObject
{
    private static readonly TreeNode Dummy = new("", "(loading)", false);

    public string Path { get; }
    public bool IsDrive { get; }

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
            if (!_iconLoaded) { _icon = string.IsNullOrEmpty(Path) ? null : Services.IconService.ForFile(Path, true); _iconLoaded = true; }
            return _icon;
        }
    }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!Set(ref _isExpanded, value)) return;
            if (value && Children.Count == 1 && Children[0] == Dummy) LoadChildren();
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public TreeNode(string path, string name, bool isDrive)
    {
        Path = path;
        _name = name;
        IsDrive = isDrive;
        if (!string.IsNullOrEmpty(path)) Children.Add(Dummy);
    }

    public void LoadChildren()
    {
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
        if (Children.Count == 1 && Children[0] == Dummy) return;   // 未展開
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

    /// <summary>
    /// ドライブ一覧をブロックせずに作る。ボリュームラベルはバックグラウンドで後から埋める
    /// （切断されたネットワークドライブで起動が固まるのを防ぐ）。
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
        return roots;
    }

    public static string DriveTypeName(DriveType t) => Services.DriveService.TypeName(t);

    /// <summary>指定パスまでツリーを展開して選択する。</summary>
    public static void RevealPath(IEnumerable<TreeNode> roots, string path)
    {
        var full = System.IO.Path.GetFullPath(path).TrimEnd('\\') + "\\";
        var root = roots.FirstOrDefault(r => full.StartsWith(r.Path, StringComparison.OrdinalIgnoreCase));
        if (root is null) return;
        var node = root;
        node.IsExpanded = true;
        var rest = full.Substring(root.Path.Length).Split('\\', StringSplitOptions.RemoveEmptyEntries);
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
