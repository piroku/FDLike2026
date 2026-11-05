using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using FDLike2026.Services;

namespace FDLike2026.ViewModels;

public sealed record KeyGuideItem(string Key, string Label);

public sealed class MainViewModel : ObservableObject
{
    public Settings Settings { get; }
    public PaneViewModel Left { get; } = new();
    public PaneViewModel Right { get; } = new();
    public ObservableCollection<TreeNode> TreeRoots { get; }

    public IReadOnlyList<KeyGuideItem> KeyGuide { get; } = new[]
    {
        new KeyGuideItem("E", "編集"), new KeyGuideItem("R", "実行"), new KeyGuideItem("T", "ツリー"),
        new KeyGuideItem("L", "装置"), new KeyGuideItem("C", "複写"), new KeyGuideItem("M", "移動"),
        new KeyGuideItem("D", "削除"), new KeyGuideItem("N", "名前"), new KeyGuideItem("A", "属性"),
        new KeyGuideItem("Q", "終了"), new KeyGuideItem("?", "ヘルプ"),
    };

    public string AppTitle => $"FDLike2026 V{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)}";

    private PaneViewModel _active;
    public PaneViewModel Active
    {
        get => _active;
        private set
        {
            if (!Set(ref _active, value)) return;
            Left.IsActive = value == Left;
            Right.IsActive = value == Right;
            OnPropertyChanged(nameof(Inactive));
        }
    }
    public PaneViewModel Inactive => Active == Left ? Right : Left;

    private bool _showTree;
    public bool ShowTree
    {
        get => _showTree;
        set { if (Set(ref _showTree, value)) Settings.ShowTree = value; }
    }

    private string _clock = "";
    public string Clock { get => _clock; private set => Set(ref _clock, value); }

    private string _message = "";
    public string Message { get => _message; set => Set(ref _message, value); }

    private readonly DispatcherTimer _timer;

    public MainViewModel()
    {
        Settings = Settings.Load();
        _showTree = Settings.ShowTree;
        TreeRoots = TreeNode.BuildRoots();

        Left.ShowHidden = Right.ShowHidden = Settings.ShowHidden;
        Left.HiddenGrouping = Right.HiddenGrouping = ParseGrouping(Settings.HiddenGrouping);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // 前回のパスが停止中のネットワークドライブだと Directory.Exists で数十秒固まるので、到達確認付きで調べる
        Left.Load(Services.PathProbe.DirectoryExists(Settings.LeftPath, 2000) ? Settings.LeftPath : "C:\\");
        Right.Load(Services.PathProbe.DirectoryExists(Settings.RightPath, 2000) ? Settings.RightPath : home);

        _active = Left;
        Left.IsActive = true;

        Clock = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Clock = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _timer.Start();
    }

    public static HiddenGrouping ParseGrouping(string s) => s switch
    {
        "mixed" => HiddenGrouping.Mixed,
        "last" => HiddenGrouping.HiddenLast,
        _ => HiddenGrouping.HiddenFirst,
    };

    public void SwitchPane() => Active = Inactive;
    public void ActivateLeft() => Active = Left;
    public void ActivateRight() => Active = Right;

    public void ToggleTree() => ShowTree = !ShowTree;

    public void SaveState()
    {
        Settings.LeftPath = Left.Path;
        Settings.RightPath = Right.Path;
        Settings.ShowTree = ShowTree;
        Settings.Save();
    }

    public void ReloadBoth()
    {
        Left.Reload();
        Right.Reload();
    }
}
