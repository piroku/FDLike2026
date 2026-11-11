using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FDLike2026.Models;
using FDLike2026.Services;
using FDLike2026.ViewModels;
using FDLike2026.Views;

namespace FDLike2026;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        ViewerWindow.AppSettings = _vm.Settings;
        DiffToolService.Setting = _vm.Settings.DiffTool;
        Width = _vm.Settings.WindowWidth;
        Height = _vm.Settings.WindowHeight;

        _vm.Left.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PaneViewModel.CursorIndex)) ScrollToCursor(LeftList, _vm.Left); };
        _vm.Right.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PaneViewModel.CursorIndex)) ScrollToCursor(RightList, _vm.Right); };
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.Active)) SyncTree(); };
        Deactivated += (_, _) => _dragItem = null;   // ダイアログを開いたらドラッグ候補を捨てる
        _vm.Left.AutoRefreshed += _ => RefreshTree();
        _vm.Right.AutoRefreshed += _ => RefreshTree();
        // 設定画面でリストの項目をいじったら即時反映（プレビュー）
        ListModeService.Changed += () =>
        {
            ApplyRightPane(ListModeService.Current.ShowRightPane);
            _vm.Left.UpdateNameColumn();
            _vm.Right.UpdateNameColumn();
            _vm.ReloadBoth();
        };
    }

    /// <summary>F5 / 自動更新: 一覧とツリーの両方をディスクの状態に合わせる。</summary>
    private void CmdRefreshAll()
    {
        GitStatusService.Invalidate();
        _vm.ReloadBoth();
        RefreshTree();
        _vm.Message = "再読込しました";
    }

    /// <summary>展開済みのツリーノードを読み直す（開閉状態は保つ）。</summary>
    private void RefreshTree()
    {
        if (!_vm.ShowTree) return;
        foreach (var root in _vm.TreeRoots) root.Refresh();
        SyncTree();
    }

    private static void ScrollToCursor(ListBox list, PaneViewModel pane)
    {
        if (pane.Current is { } cur) list.ScrollIntoView(cur);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyTreeColumn();
        ApplyListMode(Math.Clamp(_vm.Settings.StartupListMode, 0, 4), announce: false);
        SyncTree();
        ScrollToCursor(LeftList, _vm.Left);
        ScrollToCursor(RightList, _vm.Right);
        Focus();
    }

    /// <summary>監視できないフォルダ（一部のネットワークドライブ等）向けの保険: 前面に戻ったら再読込。</summary>
    private void Window_Activated(object sender, EventArgs e)
    {
        if (!IsLoaded) return;
        _vm.Left.RequestRefresh();
        _vm.Right.RequestRefresh();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _vm.Settings.WindowWidth = Width;
        _vm.Settings.WindowHeight = Height;
        _vm.SaveState();
    }

    // ---- 表示モード（カスタマイズ › リスト、Shift+1～5） --------------------

    private int _listModeIndex;
    private bool RightPaneVisible => _vm.Settings.ListModes[_listModeIndex].ShowRightPane;

    /// <summary>表示モードを切り替える。列幅・ファイル名/日付の書式・右ウィンドウの表示を反映。</summary>
    private void ApplyListMode(int index, bool announce = true)
    {
        _listModeIndex = Math.Clamp(index, 0, 4);
        ListModeService.CurrentIndex = _listModeIndex;
        var mode = _vm.Settings.ListModes[_listModeIndex];
        ListModeService.Apply(mode, _vm.Settings.DirLabel, _vm.Settings.Theme);
        ApplyRightPane(mode.ShowRightPane);
        _vm.ReloadBoth();
        if (announce)
            _vm.Message = $"表示モード {_listModeIndex + 1}: {mode.Name}" + (mode.ShowRightPane ? "" : "（右ウィンドウ 非表示）");
    }

    private void ApplyRightPane(bool show)
    {
        RightColumn.Width = show ? new GridLength(Math.Max(0.1, _vm.Settings.PaneRatio <= 0 ? 1 : 1 / _vm.Settings.PaneRatio), GridUnitType.Star) : new GridLength(0);
        PaneSplitColumn.Width = show ? new GridLength(6) : new GridLength(0);
        PaneSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RightFrame.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show && _vm.Active == _vm.Right) _vm.ActivateLeft();
    }

    /// <summary>境界のドラッグが終わったら幅を設定に記憶する。</summary>
    private void Splitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (_vm.ShowTree && TreeColumn.ActualWidth > 0) _vm.Settings.TreeWidth = TreeColumn.ActualWidth;
        if (RightPaneVisible && RightFrame.ActualWidth > 0 && LeftList.ActualWidth > 0)
            _vm.Settings.PaneRatio = Math.Clamp(LeftList.ActualWidth / RightFrame.ActualWidth, 0.1, 10);
    }

    /// <summary>Shift+F6: 現在の表示モードの「右ウィンドウを表示する」を反転して保存。</summary>
    private void CmdToggleRightPane()
    {
        var mode = _vm.Settings.ListModes[_listModeIndex];
        mode.ShowRightPane = !mode.ShowRightPane;
        _vm.Settings.Save();
        ApplyRightPane(mode.ShowRightPane);
        _vm.Message = mode.ShowRightPane ? "右ウィンドウを表示しました" : $"右ウィンドウを隠しました（{mode.Name}）";
    }

    private void CmdToggleTree() { _vm.ToggleTree(); ApplyTreeColumn(); SyncTree(); }

    private void Menu_RightPane(object sender, RoutedEventArgs e) => CmdToggleRightPane();
    private void Menu_Tree(object sender, RoutedEventArgs e) => CmdToggleTree();

    /// <summary>右ウィンドウが隠れているときは「反対側」が無いので、カレントフォルダを返す。</summary>
    private string OppositePath => RightPaneVisible ? _vm.Inactive.Path : _vm.Active.Path;

    // ---- ツリー ------------------------------------------------------------

    private bool _syncingTree;

    private void ApplyTreeColumn()
    {
        TreeColumn.Width = _vm.ShowTree ? new GridLength(Math.Clamp(_vm.Settings.TreeWidth, 80, 2000)) : new GridLength(0);
        TreeColumn.MinWidth = _vm.ShowTree ? 80 : 0;
        TreeSplitColumn.Width = _vm.ShowTree ? new GridLength(6) : new GridLength(0);
    }

    private void SyncTree()
    {
        if (!_vm.ShowTree) return;
        _syncingTree = true;
        try { TreeNode.RevealPath(_vm.TreeRoots, _vm.Active.Path); }
        finally { _syncingTree = false; }
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingTree || _treeMode) return;   // ツリーモード中は Enter で確定するまで一覧を変えない
        if (e.NewValue is TreeNode n && PathProbe.DirectoryExists(n.Path) &&
            !string.Equals(n.Path.TrimEnd('\\'), _vm.Active.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            _vm.Active.Load(n.Path);
            Focus();
        }
    }

    // ---- ツリーモード（WinFMTree 相当のキー操作） --------------------------------

    private bool _treeMode;

    private void EnterTreeMode()
    {
        if (!_vm.ShowTree) { _vm.ShowTree = true; ApplyTreeColumn(); }
        _treeMode = true;
        TreeFrame.BorderBrush = (System.Windows.Media.Brush)FindResource("InfoDataBrush");
        _vm.Left.IsActive = _vm.Right.IsActive = false;
        SyncTree();
        if (TreeCursor() is null && _vm.TreeRoots.Count > 0) SelectTreeNode(_vm.TreeRoots[0]);
        _vm.Message = "ツリー: ↑↓ 移動  → 開く  ← 閉じる  Space 開閉  Enter 決定  L 装置  K フォルダ作成  Shift+文字 頭文字ジャンプ  Esc 戻る";
    }

    private void ExitTreeMode(bool load)
    {
        _treeMode = false;
        TreeFrame.SetResourceReference(Border.BorderBrushProperty, "FrameBrush");
        _vm.Left.IsActive = _vm.Active == _vm.Left;
        _vm.Right.IsActive = _vm.Active == _vm.Right;
        if (load && TreeCursor() is { } n && PathProbe.DirectoryExists(n.Path)) { _vm.Active.Load(n.Path); }
        _vm.Message = "";
        Focus();
    }

    /// <summary>展開されている順にノードを平らに並べる（画面上の見た目の順）。</summary>
    private List<TreeNode> VisibleTreeNodes()
    {
        var list = new List<TreeNode>();
        void Walk(IEnumerable<TreeNode> nodes)
        {
            foreach (var n in nodes)
            {
                list.Add(n);
                if (n.IsExpanded) Walk(n.Children.Where(c => !string.IsNullOrEmpty(c.Path)));
            }
        }
        Walk(_vm.TreeRoots);
        return list;
    }

    private TreeNode? TreeCursor() => VisibleTreeNodes().FirstOrDefault(n => n.IsSelected);

    private void SelectTreeNode(TreeNode n)
    {
        _syncingTree = true;
        try
        {
            foreach (var v in VisibleTreeNodes()) if (v.IsSelected && v != n) v.IsSelected = false;
            n.IsSelected = true;
        }
        finally { _syncingTree = false; }
        Dispatcher.BeginInvoke(() => BringTreeNodeIntoView(n), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void BringTreeNodeIntoView(TreeNode target)
    {
        // ルートから辿ってコンテナを探し、BringIntoView
        ItemsControl parent = Tree;
        var chain = new List<TreeNode>();
        for (var n = target; n is not null; n = ParentOf(n)) chain.Insert(0, n);
        foreach (var n in chain)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(n) is not TreeViewItem tvi) { parent.UpdateLayout(); if (parent.ItemContainerGenerator.ContainerFromItem(n) is not TreeViewItem tvi2) return; tvi = tvi2; }
            if (n == target) { tvi.BringIntoView(); return; }
            parent = tvi;
        }
    }

    private TreeNode? ParentOf(TreeNode child)
    {
        TreeNode? Find(IEnumerable<TreeNode> nodes, TreeNode? par)
        {
            foreach (var n in nodes)
            {
                if (n == child) return par;
                var r = Find(n.Children, n);
                if (r is not null) return r;
            }
            return null;
        }
        return Find(_vm.TreeRoots, null);
    }

    private void TreeMoveCursor(int delta)
    {
        var list = VisibleTreeNodes();
        if (list.Count == 0) return;
        int i = list.FindIndex(n => n.IsSelected);
        i = Math.Clamp((i < 0 ? 0 : i) + delta, 0, list.Count - 1);
        SelectTreeNode(list[i]);
    }

    private bool TreeKey(Key key, bool shift, bool ctrl)
    {
        var cur = TreeCursor();
        int page = Math.Max(1, (int)(Tree.ActualHeight / ((TryFindResource("TreeFontSize") as double? ?? 16) + 6)) - 1);
        switch (key)
        {
            case Key.Escape: ExitTreeMode(load: false); return true;
            case Key.F5: foreach (var r in _vm.TreeRoots) r.Refresh(); _vm.ReloadBoth(); return true;   // ツリーも一覧も再読込
            case Key.Enter when shift: if (cur is not null) cur.IsExpanded = !cur.IsExpanded; return true;
            case Key.Enter: ExitTreeMode(load: true); return true;
            case Key.Space: if (cur is not null) cur.IsExpanded = !cur.IsExpanded; return true;
            case Key.Up: TreeMoveCursor(-1); return true;
            case Key.Down: TreeMoveCursor(1); return true;
            case Key.PageUp: TreeMoveCursor(-page); return true;
            case Key.PageDown: TreeMoveCursor(page); return true;
            case Key.Home: { var l = VisibleTreeNodes(); if (l.Count > 0) SelectTreeNode(l[0]); return true; }
            case Key.End: { var l = VisibleTreeNodes(); if (l.Count > 0) SelectTreeNode(l[^1]); return true; }
            case Key.Right:
                if (cur is not null) { if (!cur.IsExpanded) cur.IsExpanded = true; else if (cur.Children.Count > 0 && !string.IsNullOrEmpty(cur.Children[0].Path)) SelectTreeNode(cur.Children[0]); }
                return true;
            case Key.Left:
                if (cur is not null) { if (cur.IsExpanded) cur.IsExpanded = false; else if (ParentOf(cur) is { } p) SelectTreeNode(p); }
                return true;
            case Key.Tab: ExitTreeMode(load: false); return true;
            case Key.T:
            case Key.F6: ExitTreeMode(load: false); _vm.ToggleTree(); ApplyTreeColumn(); return true;
            case Key.L: CmdDrive(); return true;
            case Key.K:
                if (cur is not null)
                {
                    var name = InputDialog.Show(this, $"{cur.Name} の下に作成するフォルダ名");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        Try(() => Directory.CreateDirectory(Path.Combine(cur.Path, name)), "フォルダ作成");
                        cur.LoadChildren(); cur.IsExpanded = true;
                        var made = cur.Children.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                        if (made is not null) SelectTreeNode(made);
                    }
                }
                return true;
            case Key.F1: ShowHelp(); return true;
            case Key.Q: Close(); return true;
            case >= Key.D1 and <= Key.D9 when !shift: CmdDriveLetter((char)('A' + (key - Key.D1))); SyncTreeCursorToActive(); return true;
            case Key.D0 when !shift: CmdDriveLetter('J'); SyncTreeCursorToActive(); return true;
        }
        // Shift+英字: 同じ階層で頭文字の一致するフォルダへ
        if (shift && key >= Key.A && key <= Key.Z && cur is not null)
        {
            char c = (char)('A' + (key - Key.A));
            var siblings = (ParentOf(cur)?.Children ?? _vm.TreeRoots).Where(n => !string.IsNullOrEmpty(n.Path)).ToList();
            int i = siblings.IndexOf(cur);
            for (int k = 1; k <= siblings.Count; k++)
            {
                var n = siblings[(i + k) % siblings.Count];
                if (n.Name.StartsWith(c.ToString(), StringComparison.OrdinalIgnoreCase)) { SelectTreeNode(n); break; }
            }
            return true;
        }
        return true;   // ツリーモード中はその他のキーも一覧へ流さない
    }

    private void SyncTreeCursorToActive()
    {
        _syncingTree = true;
        try { TreeNode.RevealPath(_vm.TreeRoots, _vm.Active.Path); }
        finally { _syncingTree = false; }
        if (TreeCursor() is { } n) Dispatcher.BeginInvoke(() => BringTreeNodeIntoView(n), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ---- マウス ------------------------------------------------------------

    private void Pane_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_treeMode) ExitTreeMode(load: false);
        if (sender is FrameworkElement { DataContext: PaneViewModel p })
        {
            if (p == _vm.Left) _vm.ActivateLeft(); else _vm.ActivateRight();
        }
        Focus();
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        EnterCurrent();
    }

    // ---- 右クリックメニュー ---------------------------------------------------

    /// <summary>右クリックした行にカーソルを合わせ、そのウィンドウをアクティブにしてからメニューを出す。</summary>
    private void List_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox { DataContext: PaneViewModel pane } list) return;
        if (pane == _vm.Left) _vm.ActivateLeft(); else _vm.ActivateRight();
        if (RowAt(list, e.GetPosition(list)) is { } row)
        {
            int idx = pane.Entries.IndexOf(row);
            if (idx >= 0) pane.CursorIndex = idx;
        }
    }

    /// <summary>行の種類（.. / フォルダ / ファイル / 空白）に合わせて項目の有効・無効を切り替える。</summary>
    private void ListMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        var cur = _vm.Active.Current;
        bool any = cur is { IsParent: false };
        bool isFile = cur is { IsDirectory: false };
        bool isDir = cur is { IsDirectory: true, IsParent: false };
        if (menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == "MenuRightPane") is { } rp) rp.IsChecked = RightPaneVisible;
        void Set(string name, bool on, string? header = null)
        {
            var mi = menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Name == name);
            if (mi is null) return;
            mi.IsEnabled = on;
            if (header is not null) mi.Header = header;
        }
        Set("MenuOpen", cur is not null, isDir || cur is { IsParent: true } ? "開く(_O)　　Enter" : "実行(_O)　　Enter / R");
        Set("MenuEdit", isFile);
        Set("MenuView", isFile);
        Set("MenuCopy", any);
        Set("MenuMove", any);
        Set("MenuRename", any);
        Set("MenuDelete", any);
        Set("MenuDelPerm", any);
        Set("MenuAttr", any);
        Set("MenuExplorer", cur is not null, isDir ? "エクスプローラーで開く(_X)　　Z" : "エクスプローラーで表示(_X)　　Z");
        Set("MenuCopyPath", any);
        Set("MenuClip", any);
        Set("MenuCut", any);
        Set("MenuProps", any);
        Set("MenuPack", any);
        Set("MenuRenameSeq", any);
        Set("MenuUnpack", cur is { IsDirectory: false } && ArchiveService.IsArchive(cur.FullPath));
    }

    private void Menu_Open(object sender, RoutedEventArgs e) => EnterCurrent();
    private void Menu_Edit(object sender, RoutedEventArgs e) => CmdEdit();
    private void Menu_View(object sender, RoutedEventArgs e) => CmdView();
    private void Menu_Copy(object sender, RoutedEventArgs e) => CmdCopy();
    private void Menu_Move(object sender, RoutedEventArgs e) => CmdMove();
    private void Menu_Rename(object sender, RoutedEventArgs e) => CmdRename();
    private void Menu_Delete(object sender, RoutedEventArgs e) => CmdDelete(permanent: false);
    private void Menu_DeletePermanent(object sender, RoutedEventArgs e) => CmdDelete(permanent: true);
    private void Menu_Attr(object sender, RoutedEventArgs e) => CmdAttributes();
    private void Menu_Mkdir(object sender, RoutedEventArgs e) => CmdMkdir();
    private void Menu_PowerShell(object sender, RoutedEventArgs e) => Try(() => FileOperations.OpenPowerShell(_vm.Active.Path), "PowerShell");
    private void Menu_CopyPath(object sender, RoutedEventArgs e) => CmdCopyPath();
    private void Menu_CopyName(object sender, RoutedEventArgs e) => CmdCopyName();
    private void Menu_Refresh(object sender, RoutedEventArgs e) => CmdRefreshAll();
    private void Menu_Find(object sender, RoutedEventArgs e) => CmdFind();
    private void Menu_Grep(object sender, RoutedEventArgs e) => CmdGrep();
    private void Menu_Pack(object sender, RoutedEventArgs e) => CmdPack();
    private void Menu_RenameSeq(object sender, RoutedEventArgs e) => CmdRenameSeq();
    private void Menu_Mirror(object sender, RoutedEventArgs e) => CmdMirror();
    private void Menu_Unpack(object sender, RoutedEventArgs e) => CmdUnpack();
    private void Menu_Clip(object sender, RoutedEventArgs e) => CmdClipboard(cut: false);
    private void Menu_Cut(object sender, RoutedEventArgs e) => CmdClipboard(cut: true);
    private void Menu_Paste(object sender, RoutedEventArgs e) => CmdPaste();
    /// <summary>Windows のシェルコンテキストメニュー（エクスプローラーと同じ内容）をカーソル行の位置に出す。タグがあればタグ対象、.. / 空白なら背景メニュー。</summary>
    private void Menu_ShellMenu(object sender, RoutedEventArgs e)
    {
        var pane = _vm.Active;
        var dir = pane.Path;
        var paths = pane.TargetEntries().Select(i => i.FullPath).ToList();
        // カーソル行の左下に出す（行が見つからなければマウス位置）
        var list = pane == _vm.Left ? LeftList : RightList;
        int x, y;
        if (pane.CursorIndex >= 0 && list.ItemContainerGenerator.ContainerFromIndex(pane.CursorIndex) is FrameworkElement row && row.IsVisible)
        {
            var pt = row.PointToScreen(new Point(0, row.ActualHeight));
            x = (int)pt.X; y = (int)pt.Y;
        }
        else { var mp = System.Windows.Forms.Cursor.Position; x = mp.X; y = mp.Y; }
        // WPF のメニューが完全に閉じてから Win32 のメニューを出す
        Dispatcher.BeginInvoke(() => Try(() => ShellContextMenu.Show(this, dir, paths, x, y), "Windows のメニュー"),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Menu_Props(object sender, RoutedEventArgs e)
    {
        if (_vm.Active.Current is { IsParent: false } c) Try(() => FileOperations.ShowProperties(c.FullPath), "プロパティ");
    }

    /// <summary>フォルダならそのフォルダをエクスプローラーで開く。ファイル / .. なら現在のディレクトリを開いて選択。</summary>
    private void Menu_Explorer(object sender, RoutedEventArgs e)
    {
        var cur = _vm.Active.Current;
        Try(() =>
        {
            if (cur is { IsDirectory: true, IsParent: false }) FileOperations.ShellExecute(cur.FullPath);
            else if (cur is { IsParent: false }) FileOperations.OpenInExplorer(cur.FullPath);
            else FileOperations.ShellExecute(_vm.Active.Path);
        }, "エクスプローラー");
    }

    // ---- ドラッグ＆ドロップ ---------------------------------------------------

    private Point _dragStart;
    private FileEntry? _dragItem;

    private static FileEntry? RowAt(ListBox list, Point pt)
    {
        var hit = list.InputHitTest(pt) as DependencyObject;
        while (hit is not null && hit is not ListBoxItem) hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        return (hit as ListBoxItem)?.DataContext as FileEntry;
    }

    /// <summary>クリック = そのウィンドウをアクティブにし、その行へカーソル移動（キーでカーソルを合わせたのと同じ状態にする）。
    /// Shift+クリック = カーソル行からその行までを範囲タグ（WinFM2008 と同じ）、Ctrl+クリック = その行のタグを反転。</summary>
    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_treeMode) ExitTreeMode(load: false);
        if (sender is not ListBox { DataContext: PaneViewModel pane } list) return;
        bool wasActive = pane == _vm.Active;
        if (pane == _vm.Left) _vm.ActivateLeft(); else _vm.ActivateRight();
        _dragStart = e.GetPosition(list);
        _dragItem = RowAt(list, _dragStart);
        if (_dragItem is not null)
        {
            int idx = pane.Entries.IndexOf(_dragItem);
            if (idx >= 0)
            {
                var mod = Keyboard.Modifiers;
                if ((mod & ModifierKeys.Shift) != 0 && wasActive) pane.TagRangeTo(idx);   // 反対側をクリックしたときは範囲にしない
                else if ((mod & ModifierKeys.Control) != 0) pane.ToggleTagAt(idx);
                else pane.CursorIndex = idx;
                if (mod != ModifierKeys.None) _dragItem = null;   // 修飾キー付きクリックではドラッグを始めない
            }
        }
        Focus();
    }

    /// <summary>一覧の幅が変わったら（右ウィンドウの表示切替・境界ドラッグ・ウィンドウサイズ変更）名前列の上限を更新。</summary>
    private void List_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ListBox { DataContext: PaneViewModel pane } list) pane.ViewportWidth = list.ActualWidth;
    }

    /// <summary>ボタンを離したらドラッグ候補を捨てる（ダイアログを挟んで戻ってきたときに古い候補でドラッグが始まらないように）。</summary>
    private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragItem = null;

    /// <summary>ファイル行を一定距離ドラッグしたら、FileDrop 形式で OLE ドラッグを開始（Teams・エクスプローラー等へ渡せる）。</summary>
    private void List_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null || _dragItem.IsParent) return;
        if (!IsActive) { _dragItem = null; return; }   // 他のウィンドウ（JumpFolder 等）から戻った直後のボタン押下は無視
        if (sender is not ListBox { DataContext: PaneViewModel pane } list) return;
        var pos = e.GetPosition(list);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        // 掴んだ行がタグ付きならタグ全部、そうでなければその 1 件
        var items = _dragItem.Tagged ? pane.TargetEntries() : new[] { _dragItem };
        var paths = new System.Collections.Specialized.StringCollection();
        foreach (var it in items) paths.Add(it.FullPath);
        var data = new DataObject();
        data.SetFileDropList(paths);
        data.SetText(string.Join(Environment.NewLine, items.Select(i => i.FullPath)));
        _dragItem = null;
        DragDrop.DoDragDrop(list, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        _vm.ReloadBoth();
    }

    private static DragDropEffects EffectFor(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return DragDropEffects.None;
        // Shift: 移動 / それ以外: 複写（Ctrl でも複写）
        bool shift = (e.KeyStates & DragDropKeyStates.ShiftKey) != 0;
        var want = shift ? DragDropEffects.Move : DragDropEffects.Copy;
        return (e.AllowedEffects & want) != 0 ? want : (e.AllowedEffects & DragDropEffects.Copy);
    }

    private void List_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = EffectFor(e);
        e.Handled = true;
    }

    private void List_Drop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox { DataContext: PaneViewModel pane } list) return;
        var effect = EffectFor(e);
        if (effect == DragDropEffects.None) return;
        // フォルダ行の上に落としたらそのフォルダへ、それ以外はこのウィンドウのディレクトリへ
        var row = RowAt(list, e.GetPosition(list));
        var dest = row is { IsDirectory: true } ? row.FullPath : pane.Path;
        DoDrop(e, dest, effect);
        e.Handled = true;
    }

    private void Tree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TreeNodeAt(e) is null ? DragDropEffects.None : EffectFor(e);
        e.Handled = true;
    }

    private void Tree_Drop(object sender, DragEventArgs e)
    {
        var node = TreeNodeAt(e);
        var effect = EffectFor(e);
        if (node is null || effect == DragDropEffects.None) return;
        DoDrop(e, node.Path, effect);
        e.Handled = true;
    }

    private TreeNode? TreeNodeAt(DragEventArgs e)
    {
        var hit = Tree.InputHitTest(e.GetPosition(Tree)) as DependencyObject;
        while (hit is not null && hit is not TreeViewItem) hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        return (hit as TreeViewItem)?.DataContext as TreeNode;
    }

    private void DoDrop(DragEventArgs e, string destDir, DragDropEffects effect)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        var items = FileOperations.FromPaths(paths);
        // 自分自身や、同じディレクトリへの移動は無視
        items = items.Where(i => !string.Equals(Path.GetDirectoryName(i.FullPath.TrimEnd('\\')), destDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                                 && !destDir.StartsWith(i.FullPath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)).ToList();
        if (items.Count == 0) return;
        bool move = effect == DragDropEffects.Move;
        var verb = move ? "移動" : "複写";
        if (!Confirm($"{Describe(items)} を\n{destDir}\nへ{verb}しますか？")) return;
        Try(() => { if (move) FileOperations.Move(items, destDir); else FileOperations.Copy(items, destDir); }, verb);
        _vm.ReloadBoth();
    }

    // ---- キー --------------------------------------------------------------

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var pane = _vm.Active;
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (_treeMode) { e.Handled = TreeKey(key, shift, ctrl); return; }

        double rowH = (TryFindResource("ListFontSize") as double? ?? 16) + 4;
        int page = Math.Max(1, (int)(LeftList.ActualHeight / rowH) - 1);
        bool handled = true;

        if (alt)
        {
            switch (key)
            {
                case Key.Left: pane.GoBack(); SyncTree(); break;        // Alt+←  前に戻る
                case Key.Right: pane.GoForward(); SyncTree(); break;    // Alt+→  次に進む
                default: handled = false; break;
            }
        }
        else if (ctrl && shift)
        {
            switch (key)
            {
                case Key.C: CmdDrive(); break;                                          // マイコンピュータへ → ドライブ選択
                case Key.B: CmdCopyName(); break;                                       // コピー(ファイル名だけ)
                default: handled = false; break;
            }
        }
        else if (ctrl)
        {
            switch (key)
            {
                // ---- WinFM2008 の Ctrl+キー ----
                case Key.A: pane.TagAll(true); break;                                   // 全て選択
                case Key.B: CmdCopyPath(); break;                                       // コピー(フルパス)
                case Key.C: CmdClipboard(cut: false); break;                            // コピー
                case Key.X: CmdClipboard(cut: true); break;                             // 切り取り
                case Key.V: CmdPaste(); break;                                          // 貼り付け
                case Key.Insert: CmdClipboard(cut: false); break;                       // コピー
                case Key.D: CmdDirSize(); break;                                        // サブフォルダのサイズ
                case Key.M: CmdMirror(); break;                                         // ミラーコピー
                case Key.G: Try(FileOperations.OpenRecycleBin, "ごみ箱"); break;         // ごみ箱を開く
                case Key.H: CmdRecent(); break;                                         // 最近開いたフォルダ
                case Key.I: CmdToggleIcons(); break;                                    // アイコンの表示 / 非表示
                case Key.K: CmdTagWildcard(); break;                                    // ワイルドカードで選択
                case Key.N: CmdNewFile(); break;                                        // 新規ファイル
                case Key.T: CmdTimestamp(); break;                                      // タイムスタンプ変更
                case Key.U: CmdCase(); break;                                           // 大文字小文字変換
                case Key.W: pane.TagFilesOnly(); break;                                 // ファイルを全て選択
                case Key.Z: pane.ReverseTags(); break;                                  // タグ反転
                case Key.Home: pane.CursorHome(); break;
                case Key.End: pane.CursorEnd(); break;
                case Key.Delete: CmdDelete(permanent: true); break;                     // 完全削除（FDLike 独自）
                case Key.J: CmdJumpPath(); break;                                       // 互換（旧割り当て）
                case Key.R: pane.ReverseTags(); break;                                  // 互換（旧割り当て）
                default: handled = false; break;
            }
        }
        else if (shift)
        {
            switch (key)
            {
                // ---- WinFM2008 の Shift+キー ----
                case Key.D: pane.ShowHidden = !pane.ShowHidden; _vm.Settings.ShowHidden = pane.ShowHidden; break; // 特殊属性の表示切替
                case Key.G: Try(FileOperations.EmptyRecycleBin, "ごみ箱を空にする"); break;
                case Key.I: CmdJump(); break;                                           // イニシャルサーチ
                case Key.P: if (pane.Current is { IsParent: false } pp) Try(() => FileOperations.ShowProperties(pp.FullPath), "プロパティ"); break;
                case Key.S: WindowState = WindowState.Minimized; break;
                case Key.Home: pane.ToggleTagAllFiles(includeDirs: true); break;        // フォルダ含め全マーク
                case Key.End: pane.CursorEnd(); break;
                case Key.OemComma: pane.CursorHome(); break;                            // Shift+, (<) 先頭
                case Key.OemPeriod: pane.CursorEnd(); break;                            // Shift+. (>) 最後
                case Key.OemQuestion: ShowHelp(); break;                                // ?
                case Key.F6: CmdToggleRightPane(); break;                               // 右ウィンドウの表示 / 非表示
                case Key.E: CmdEdit(); break;                                           // バイナリエディタ（未設定時はエディタ）
                case Key.N: CmdRenameSeq(); break;                                      // 名前の一括変換（FDLike 独自割り当て）
                case Key.F: CmdGrep(); break;                                           // 文字列の検索（grep）
                case Key.W: CmdCompare(); break;                                        // 比較（WinMerge など、FDLike 独自）
                case >= Key.D1 and <= Key.D5: ApplyListMode(key - Key.D1); break;       // 表示モード 1～5
                default: handled = false; break;
            }
        }
        else
        {
            switch (key)
            {
                case Key.Up: pane.MoveCursor(-1); break;
                case Key.Down: pane.MoveCursor(1); break;
                case Key.PageUp: pane.MoveCursor(-page); break;
                case Key.PageDown: pane.MoveCursor(page); break;
                case Key.Home: pane.ToggleTagAllFiles(includeDirs: false); break;        // 全ファイルにタグ / 解除（WinFM）
                case Key.End: pane.CursorEnd(); break;
                case Key.Left: _vm.ActivateLeft(); break;
                case Key.Right: if (RightPaneVisible) _vm.ActivateRight(); break;
                case Key.Tab: if (RightPaneVisible) _vm.SwitchPane(); break;
                case Key.Enter: EnterCurrent(); break;
                case Key.Back: GoParent(); break;
                case Key.Oem5:          // JP キーボードの ￥ / US の \
                case Key.OemBackslash:  // JP キーボードの ＼(ろ)
                    pane.Load(Path.GetPathRoot(pane.Path) ?? pane.Path); SyncTree(); break;
                case Key.Space: pane.ToggleTagAndAdvance(); break;
                case Key.Delete: CmdDelete(permanent: false); break;
                case Key.Insert: pane.ToggleTagAndAdvance(); break;

                // ---- ファンクションキー（WinFM2008 準拠） ----
                case Key.F1: ShowHelp(); break;
                case Key.F3: Try(() => FileOperations.OpenTerminal(pane.Path), "DOS シェル"); break;
                case Key.F5: CmdRefreshAll(); break;
                case Key.F6: CmdToggleTree(); break;
                case Key.F10: CmdSettings(); break;
                case Key.F2: CmdMacro(); break;                                         // コマンドマクロ
                case Key.F4: CmdAssoc(); break;                                         // 拡張子の対応

                // ---- 数字キー: 1～9, 0 → A～I, J ドライブ ----
                case >= Key.D1 and <= Key.D9: CmdDriveLetter((char)('A' + (key - Key.D1))); break;
                case Key.D0: CmdDriveLetter('J'); break;

                // ---- 英字キー（WinFM2008 準拠） ----
                case Key.A: CmdAttributes(); break;
                case Key.B: Try(() => FileOperations.OpenPowerShell(pane.Path), "PowerShell"); break;
                case Key.C: CmdCopy(); break;
                case Key.D: CmdDelete(permanent: false); break;
                case Key.E: CmdEdit(); break;
                case Key.F: CmdFind(); break;
                case Key.H: CmdJumpPath(); break;                                       // カレントフォルダの変更（パス入力）
                case Key.J: CmdJumpFolder(); break;
                case Key.K: CmdMkdir(); break;
                case Key.L: CmdDrive(); break;
                case Key.M: CmdMove(); break;
                case Key.N: CmdRename(); break;
                case Key.O: if (RightPaneVisible) _vm.Inactive.Load(pane.Path); break;  // 反対側を同じフォルダに（FDLike 独自）
                case Key.P: CmdPack(); break;                                           // 圧縮
                case Key.U: CmdUnpack(); break;                                         // 解凍
                case Key.Q: Close(); break;
                case Key.R: CmdRun(); break;
                case Key.S: CmdSort(); break;
                case Key.T: EnterTreeMode(); break;                                     // ツリーへ（WinFMTree 相当）
                case Key.V: CmdView(); break;
                case Key.X: CmdExec(); break;                                           // コマンドの実行
                case Key.Y: CmdMask(); break;                                           // ワイルドカード（マスク）
                case Key.W: CmdMask(); break;                                           // 互換（旧割り当て）
                case Key.Z: if (pane.Current is { } z) FileOperations.OpenInExplorer(z.FullPath); break;
                default: handled = false; break;
            }
        }
        e.Handled = handled;
    }

    // ---- 各コマンド ----------------------------------------------------------

    private void EnterCurrent()
    {
        var pane = _vm.Active;
        var cur = pane.Current;
        if (cur is null) return;
        if (cur.IsParent) { GoParent(); return; }
        if (cur.IsDirectory)
        {
            pane.Load(cur.FullPath);
            SyncTree();
            return;
        }
        // F4 の「拡張子の対応」が最優先、無ければシステムの関連付けで開く（エクスプローラーのダブルクリックと同じ）。
        // 「コマンドの実行」画面（DOS 窓が出る）は R / X のときだけ。
        if (AssocWindow.FindCommand(_vm.Settings, cur.Name) is { } macro) { RunMacro(macro, "拡張子の対応", keepWindow: false); return; }
        Try(() => FileOperations.ShellExecute(cur.FullPath, pane.Path), "実行");
    }

    private void GoParent()
    {
        var pane = _vm.Active;
        var parent = Directory.GetParent(pane.Path);
        if (parent is null) return;
        var childName = Path.GetFileName(pane.Path.TrimEnd('\\'));
        pane.Load(parent.FullName, childName);
        SyncTree();
    }

    /// <summary>R: 実行。WinFM と同じく、まず「コマンドの実行」画面を開き（カーソル位置のファイルのフルパスが入った状態）、OK で実行する。
    /// .. や何もない所でも画面を開く（アプリケーション名にはカレントフォルダのフルパスが入る）。</summary>
    private void CmdRun() => CmdExec();   // フォルダでも WinFM と同じく画面を開く

    private void CmdEdit()
    {
        var cur = _vm.Active.Current;
        if (cur is null || cur.IsDirectory)
        {
            // WinFM2008 と同じ: .. やフォルダ、何もない所で E → ファイル名を聞いて（新規なら作って）エディタで開く
            var name = InputDialog.Show(this, "ファイル名（カレントフォルダに作成してエディタで開きます。既にあればそれを開きます）", "", false, "ファイルの編集");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim().Trim('"');
            var path = Path.IsPathRooted(name) ? name : Path.Combine(_vm.Active.Path, name);
            Try(() =>
            {
                if (Directory.Exists(path)) throw new IOException($"{name} はフォルダです");
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, Array.Empty<byte>());
                    _vm.Active.Reload(Path.GetFileName(path));
                }
                FileOperations.OpenInEditor(_vm.Settings.EditorPath, path);
            }, "ファイルの編集");
            return;
        }
        Try(() => FileOperations.OpenInEditor(_vm.Settings.EditorPath, cur.FullPath), "編集");
    }

    private void CmdView()
    {
        var cur = _vm.Active.Current;
        if (cur is null || cur.IsDirectory) return;
        if (ArchiveService.IsArchive(cur.FullPath))
        {
            Try(() => new ViewerWindow(this, $"{cur.FullPath}  [書庫の内容]", ArchiveService.ListText(cur.FullPath), true).ShowDialog(), "書庫の表示");
            return;
        }
        Try(() => new ViewerWindow(this, cur.FullPath, _vm.Settings.EditorPath).ShowDialog(), "ビューア");
    }

    private void CmdDrive()
    {
        // ツリーのルート（ラベルはバックグラウンドで解決済み）をそのまま使う。
        // 切断されたネットワークドライブで固まらないよう、ここでは IsReady / 空き容量を問い合わせない。
        var choices = _vm.TreeRoots
            .Select(n => new ListChoice(n.Path.Substring(0, 1), n.Name, n.Path))
            .ToList();
        var cur = Path.GetPathRoot(_vm.Active.Path) ?? "";
        int idx = Math.Max(0, choices.FindIndex(c => string.Equals((string)c.Value!, cur, StringComparison.OrdinalIgnoreCase)));
        var sel = ListDialog.Show(this, "装置（ドライブ）を選択してください", choices, idx);
        if (sel?.Value is string root) { _vm.Active.Load(root); SyncTree(); }
    }

    private void CmdCopy() => CopyOrMove(move: false);
    private void CmdMove() => CopyOrMove(move: true);

    /// <summary>C / M: WinFM2008 準拠の複写・移動ダイアログ → 自前エンジンで実行（上書き確認・更新のみ・フィルタ・進捗・中止）。</summary>
    private void CopyOrMove(bool move)
    {
        var items = _vm.Active.TargetEntries();
        if (items.Count == 0) return;
        var dlg = new CopyMoveDialog(this, _vm.Settings, move, items, _vm.Active.Path, OppositePath);
        if (dlg.ShowDialog() != true) return;
        var o = dlg.Options;
        var verb = move ? "移動" : "複写";

        // 同じフォルダへの複写・移動（WinFM2008 と同じく「名前の変更」を 1 件ずつ聞く。スキップで除外、キャンセルで中止）
        if (SameDir(o.DestDir, _vm.Active.Path) && string.IsNullOrWhiteSpace(o.RenameTo))
        {
            var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var kept = new List<FileEntry>();
            foreach (var it in items)
            {
                var rd = new RenameCopyDialog(this, it, move);
                rd.ShowDialog();
                if (rd.Result == RenameCopyDialog.Choice.Cancel) { _vm.Message = $"{verb}: 中止"; return; }
                if (rd.Result == RenameCopyDialog.Choice.Skip) continue;
                renames[it.FullPath] = rd.NewNameText;
                kept.Add(it);
            }
            if (kept.Count == 0) { _vm.Message = $"{verb}: 対象がありません"; return; }
            items = kept;
            o.Renames = renames;
        }

        var pw = new ProgressWindow(this, $"ファイルの{verb}", $"{o.DestDir} へ{verb}中...");
        CopyEngine.AskOverwrite ask = (src, dst) => Dispatcher.Invoke(() =>
        {
            var d = new OverwriteDialog(pw, src, dst);
            d.ShowDialog();
            return (d.Result, d.ApplyAll);
        });
        bool ok = pw.Run((progress, token) => CopyEngine.Run(items, o, progress, ask, token));
        if (pw.Error is not null)
        {
            _vm.Message = $"{verb}: エラー - {pw.Error.Message}";
            MessageBox.Show(this, pw.Error.Message, $"FDLike2026 - {verb}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else _vm.Message = ok ? $"{verb}: 完了" : $"{verb}: 中止";

        _vm.Active.TagAll(false);
        _vm.ReloadBoth();
        if (ok && dlg.GoAfter && Directory.Exists(o.DestDir)) { _vm.Active.Load(o.DestDir); SyncTree(); }
    }

    private static bool SameDir(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private void CmdDelete(bool permanent)
    {
        var items = _vm.Active.TargetEntries();
        if (items.Count == 0) return;
        bool recycle = !permanent && _vm.Settings.DeleteToRecycleBin;
        // 共有フォルダ（UNC / ネットワークドライブ）にはごみ箱が無い。「ごみ箱へ」のつもりで消すと戻せないので、確認の文言を変える
        bool noBin = recycle && items.Any(i => !FileOperations.HasRecycleBin(i.FullPath));
        var how = !recycle ? "完全に削除" : noBin ? "完全に削除（ネットワーク上のためごみ箱には入りません）" : "ごみ箱へ移動";
        if (!Confirm($"{Describe(items)} を{how}しますか？")) return;
        Try(() => FileOperations.Delete(items, recycle), "削除");
        _vm.ReloadBoth();
    }

    private void CmdRename()
    {
        var cur = _vm.Active.Current;
        if (cur is null || cur.IsParent) return;
        var name = InputDialog.Show(this, "新しい名前", cur.Name, selectBaseNameOnly: !cur.IsDirectory);
        if (name is null) return;
        Try(() => FileOperations.Rename(cur, name), "名前変更");
        _vm.Active.Reload(name);
    }

    private void CmdAttributes()
    {
        var items = _vm.Active.TargetEntries();
        if (items.Count == 0) return;
        var dlg = new AttributeDialog(this, $"{Describe(items)} の属性", items.Count == 1 ? items[0].Attributes : null);
        if (dlg.ShowDialog() != true) return;
        Try(() => FileOperations.SetAttributes(items, dlg.SetFlags, dlg.ClearFlags), "属性変更");
        _vm.Active.NotifyCounts();
    }

    private void CmdMkdir()
    {
        var name = InputDialog.Show(this, "作成するディレクトリ名");
        if (string.IsNullOrWhiteSpace(name)) return;
        var full = Path.Combine(_vm.Active.Path, name);
        Try(() => Directory.CreateDirectory(full), "ディレクトリ作成");
        _vm.Active.Reload(name);
        SyncTree();
    }

    private void CmdMask()
    {
        var m = InputDialog.Show(this, "ファイルマスク（例: *.txt  *.log）", _vm.Active.Mask);
        if (m is null) return;
        _vm.Active.Mask = m;
    }

    /// <summary>S: オブジェクトのソート（WinFM2008 と同じダイアログ。キー → 昇順 / 降順）。</summary>
    private void CmdSort()
    {
        var pane = _vm.Active;
        var dlg = new SortDialog(this, pane.Sort, pane.SortDescending);
        if (dlg.ShowDialog() != true) return;
        pane.SetSort(dlg.Mode, dlg.Descending);
        _vm.Message = $"ソート: {pane.SortText}";
    }

    private void CmdJump()
    {
        var s = InputDialog.Show(this, "ジャンプ: 名前の先頭文字");
        if (!string.IsNullOrEmpty(s)) _vm.Active.JumpTo(s);
    }

    /// <summary>J: WinFM2008 の JumpFolder。登録済みのパスへ 1 キーで移動。</summary>
    private void CmdJumpFolder()
    {
        var w = new JumpWindow(this, _vm.Settings, _vm.Active.Path);
        w.ShowDialog();
        if (w.SelectedPath is { } p)
        {
            _vm.Active.Load(p, w.SelectName);
            SyncTree();
            _vm.Message = $"ジャンプ: {p}";
        }
    }

    // ---- WinFM2008 準拠で追加したコマンド ----------------------------------------

    private void CmdClipboard(bool cut)
    {
        var items = _vm.Active.TargetEntries();
        if (items.Count == 0) return;
        Try(() => FileOperations.CopyToClipboard(items, cut), cut ? "切り取り" : "コピー");
        _vm.Message = $"{Describe(items)} をクリップボードへ{(cut ? "切り取り" : "コピー")}しました（Ctrl+V で貼り付け。エディタに貼るとファイル名の一覧）";
    }

    private void CmdPaste()
    {
        var dest = _vm.Active.Path;
        Try(() =>
        {
            int n;
            try
            {
                n = FileOperations.PasteFromClipboard(dest, (item, move) =>
                {
                    var rd = new RenameCopyDialog(this, item, move);
                    rd.ShowDialog();
                    return rd.Result switch
                    {
                        RenameCopyDialog.Choice.Ok => rd.NewNameText,
                        RenameCopyDialog.Choice.Skip => null,
                        _ => throw new OperationCanceledException(),
                    };
                });
            }
            catch (OperationCanceledException) { _vm.Message = "貼り付け: 中止"; return; }
            _vm.Message = n == 0 ? "クリップボードにファイルがありません（または全てスキップ）" : $"{n} 件を貼り付けました";
        }, "貼り付け");
        _vm.ReloadBoth();
    }

    private void CmdToggleIcons()
    {
        _vm.Settings.ShowIcons = !_vm.Settings.ShowIcons;
        ThemeService.ApplyIcons(_vm.Settings.ShowIcons);
        _vm.Settings.Save();
        _vm.Message = _vm.Settings.ShowIcons ? "アイコン: 表示" : "アイコン: 非表示";
    }

    private void CmdDirSize()
    {
        var items = _vm.Active.TargetEntries().Where(i => i.IsDirectory).ToList();
        if (items.Count == 0) { _vm.Message = "フォルダにカーソルを合わせてください"; return; }
        _vm.Message = "サイズを計算中...";
        var paths = items.Select(i => i.FullPath).ToList();
        Task.Run(() => paths.Select(p => (p, FileOperations.DirectorySize(p))).ToList()).ContinueWith(t =>
        {
            Dispatcher.Invoke(() =>
            {
                var lines = t.Result.Select(r => $"{Path.GetFileName(r.p)}  {r.Item2:#,0} バイト ({r.Item2 / (1024.0 * 1024):#,0.0} MB)");
                var total = t.Result.Sum(r => r.Item2);
                _vm.Message = $"サブフォルダのサイズ: 合計 {total:#,0} バイト";
                MessageBox.Show(this, string.Join("\n", lines) + (t.Result.Count > 1 ? $"\n\n合計 {total:#,0} バイト" : ""), "サブフォルダのサイズ", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        });
    }

    private void CmdRecent()
    {
        // 履歴に停止中のネットワークパスが混ざっていても固まらないよう到達確認付きで絞る
        var list = PaneViewModel.Recent.Where(p => PathProbe.DirectoryExists(p)).Take(36).ToList();
        if (list.Count == 0) { _vm.Message = "履歴がありません"; return; }
        var keys = "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ0";
        var choices = list.Select((p, i) => new ListChoice(keys[i].ToString(), p, p)).ToList();
        var sel = ListDialog.Show(this, "最近開いたフォルダ", choices);
        if (sel?.Value is string p) { _vm.Active.Load(p); SyncTree(); }
    }

    private void CmdTagWildcard()
    {
        var p = InputDialog.Show(this, "タグを付けるファイルのワイルドカード（スペース区切りで複数可）", "*.");
        if (string.IsNullOrWhiteSpace(p)) return;
        int n = _vm.Active.TagByWildcard(p);
        _vm.Message = $"{n} 件にタグを付けました";
    }

    private void CmdNewFile()
    {
        var name = InputDialog.Show(this, "作成するファイル名", "新規テキスト.txt", selectBaseNameOnly: true);
        if (string.IsNullOrWhiteSpace(name)) return;
        var full = Path.Combine(_vm.Active.Path, name);
        Try(() => FileOperations.CreateEmptyFile(full), "新規ファイル");
        _vm.Active.Reload(name);
    }

    private void CmdTimestamp()
    {
        var items = _vm.Active.TargetEntries();
        if (items.Count == 0) return;
        var init = items.Count == 1 ? items[0].LastWrite.ToString("yyyy/MM/dd HH:mm:ss") : DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
        var s = InputDialog.Show(this, $"{Describe(items)} の更新日時（yyyy/MM/dd HH:mm:ss、空なら現在時刻）", init);
        if (s is null) return;
        DateTime when;
        if (string.IsNullOrWhiteSpace(s)) when = DateTime.Now;
        else if (!DateTime.TryParse(s, out when)) { _vm.Message = "日時の形式が不正です"; return; }
        Try(() => FileOperations.SetTimestamp(items, when), "タイムスタンプ変更");
        _vm.Active.Reload();
    }

    private void CmdCase()
    {
        var items = _vm.Active.TargetEntries();
        if (items.Count == 0) return;
        var choices = new[]
        {
            new ListChoice("U", "すべて大文字", "upper"),
            new ListChoice("L", "すべて小文字", "lower"),
            new ListChoice("E", "拡張子だけ小文字", "ext-lower"),
            new ListChoice("C", "先頭だけ大文字（他は小文字）", "capital"),
        };
        var sel = ListDialog.Show(this, $"{Describe(items)} の名前の大文字・小文字変換", choices);
        if (sel?.Value is not string mode) return;
        Try(() => { int n = FileOperations.ConvertCase(items, mode); _vm.Message = $"{n} 件を変換しました"; }, "大文字小文字変換");
        _vm.Active.Reload();
    }

    private MacroExpander.Context MacroContext() => new(
        _vm.Active.Path,
        _vm.Active.Current is { IsParent: false } c ? c : null,
        _vm.Active.Entries.Where(e => e.Tagged).ToList(),
        prompt => InputDialog.Show(this, prompt),
        _vm.Inactive.Path,
        _vm.Inactive.Current is { IsParent: false } oc ? oc : null);

    /// <summary>
    /// Shift+W: 比較（WinMerge など）。タグ 2 件ならその 2 ファイル、
    /// カーソルがファイルなら反対ペインの同名ファイル（なければ反対側のカーソル位置）、
    /// カーソルがフォルダ / .. なら左右のフォルダを比較する。
    /// </summary>
    private void CmdCompare()
    {
        var pane = _vm.Active;
        var tool = DiffToolService.Resolve(_vm.Settings.DiffTool);
        if (tool is null)
        {
            _vm.Message = "比較ツールが見つかりません。設定 → 操作 → 比較ツール に WinMergeU.exe などのパスを入れてください";
            return;
        }
        var tagged = pane.Entries.Where(e => e.Tagged).ToList();
        string left, right; bool dir;
        if (tagged.Count == 2)
        {
            left = tagged[0].FullPath; right = tagged[1].FullPath; dir = tagged[0].IsDirectory;
        }
        else if (tagged.Count > 2)
        {
            _vm.Message = "比較はタグ 2 件まで（または タグなしでカーソル位置）です";
            return;
        }
        else if (!RightPaneVisible)
        {
            _vm.Message = "右ペインが非表示です。ファイルを 2 件タグして Shift+W するか、Shift+F6 で右ペインを表示してください";
            return;
        }
        else if (pane.Current is { IsParent: false, IsDirectory: false } cur)
        {
            left = cur.FullPath;
            var same = Path.Combine(_vm.Inactive.Path, cur.Name);
            if (File.Exists(same)) right = same;
            else if (_vm.Inactive.Current is { IsParent: false, IsDirectory: false } oc) right = oc.FullPath;
            else { _vm.Message = $"反対側に {cur.Name} がなく、反対側のカーソルもファイルではありません"; return; }
            dir = false;
        }
        else
        {
            left = pane.Path; right = _vm.Inactive.Path; dir = true;
        }
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)) { _vm.Message = "左右が同じ対象です"; return; }
        _vm.Message = $"比較: {left}  ⇔  {right}";
        Try(() => DiffToolService.Launch(tool, left, right, dir, pane.Path), "比較");
    }

    /// <summary>マクロを展開して実行する（X / F2 / F4 共通）。</summary>
    private void RunMacro(string macro, string what, bool keepWindow)
    {
        var expanded = MacroExpander.Expand(macro, MacroContext());
        if (expanded is null) return;   // $R でキャンセル
        Try(() => MacroExpander.Run(expanded, _vm.Active.Path, keepWindow), what);
    }

    private void CmdExec()
    {
        // アプリケーション名の初期値はフルパス（WinFM の「ファイル名を指定して実行」と同じ）。.. や何もない所ならカレントフォルダ。
        var initial = _vm.Active.Current is { IsParent: false } c ? c.FullPath : _vm.Active.Path;
        var dlg = new ExecDialog(this, _vm.Settings, initial);
        if (dlg.ShowDialog() != true) return;
        var app = dlg.App; var args = dlg.Args;
        // $ か ; を含めばマクロとして展開
        if (app.Contains('$') || app.Contains(';') || args.Contains('$'))
        {
            var ctx = MacroContext();
            var ea = MacroExpander.Expand(app, ctx); if (ea is null) return;
            var eg = MacroExpander.Expand(args, ctx); if (eg is null) return;
            if (ea.Contains(';')) { Try(() => MacroExpander.Run(ea, _vm.Active.Path, keepWindow: !dlg.NoPause), "コマンド実行"); return; }
            app = ea; args = eg;
        }
        Try(() => ExecDialog.Execute(app, args, _vm.Active.Path, dlg.Dos, dlg.NoPause, dlg.RunAs), "コマンド実行");
    }

    /// <summary>Shift+N: 名前の一括変換（連番）。</summary>
    private void CmdRenameSeq()
    {
        var items = _vm.Active.TargetEntries();
        if (items.Count == 0) return;
        var dlg = new RenameSeqDialog(this, items);
        if (dlg.ShowDialog() != true) return;
        var plans = dlg.Plans.Where(p => p.OldName != p.NewName).ToList();
        Try(() =>
        {
            var dir = _vm.Active.Path;
            if (dlg.ConsiderExisting)
            {
                // 2 段階: 一旦すべて一時名にしてから最終名へ（A→B, B→A のような入れ替えにも対応）
                var temps = new List<(RenamePlan p, string tmp)>();
                foreach (var p in plans)
                {
                    var tmp = Path.Combine(dir, p.OldName + ".~ren" + Guid.NewGuid().ToString("N")[..6]);
                    if (p.Entry.IsDirectory) Directory.Move(p.Entry.FullPath, tmp); else File.Move(p.Entry.FullPath, tmp);
                    temps.Add((p, tmp));
                }
                foreach (var (p, tmp) in temps)
                {
                    var dst = Path.Combine(dir, p.NewName);
                    if (File.Exists(dst) || Directory.Exists(dst)) dst = CopyEngine.UniqueName(dst);
                    if (p.Entry.IsDirectory) Directory.Move(tmp, dst); else File.Move(tmp, dst);
                }
            }
            else
            {
                foreach (var p in plans)
                {
                    var dst = Path.Combine(dir, p.NewName);
                    if (p.Entry.IsDirectory) Directory.Move(p.Entry.FullPath, dst); else File.Move(p.Entry.FullPath, dst);
                }
            }
            _vm.Message = $"名前の一括変換: {plans.Count} 件";
        }, "名前の一括変換");
        _vm.Active.TagAll(false);
        _vm.Active.Reload();
    }

    /// <summary>Ctrl+M: ミラーコピー（robocopy）。</summary>
    private void CmdMirror()
    {
        var dlg = new MirrorDialog(this, _vm.Settings, _vm.Active.Path, OppositePath);
        if (dlg.ShowDialog() != true) return;
        var p = dlg.Proc;
        Try(() =>
        {
            if (p.Confirm)
            {
                // /L で一覧だけ取り、確認してから実行
                var psi = new System.Diagnostics.ProcessStartInfo("robocopy.exe", MirrorDialog.BuildArgs(p, dryRun: true))
                { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.Unicode };
                using var proc = System.Diagnostics.Process.Start(psi)!;
                var text = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                new ViewerWindow(this, $"ミラーコピーの確認（実行前の一覧）: {p.Src} → {p.Dst}", text, true).ShowDialog();
                if (!Confirm($"{p.Src}\n  → {p.Dst}\nミラーコピーを実行しますか？{(p.Delete ? "\n（複写元に無いファイルは複写先から削除されます）" : "")}")) return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
                "/k robocopy " + MirrorDialog.BuildArgs(p, dryRun: false)) { UseShellExecute = true });
            _vm.Message = "ミラーコピーを開始しました（robocopy のウィンドウで進行を確認してください）";
        }, "ミラーコピー");
    }

    private void CmdMacro()
    {
        var w = new MacroWindow(this, _vm.Settings, _vm.Active.Path);
        w.ShowDialog();
        if (w.SelectedMacro is { } m) RunMacro(m, "マクロ実行", keepWindow: false);
    }

    private void CmdAssoc() => new AssocWindow(this, _vm.Settings).ShowDialog();

    private void CmdDriveLetter(char letter)
    {
        var root = letter + ":\\";
        if (PathProbe.IsUnreachable(root)) { _vm.Message = $"ドライブ {letter}: に接続できません（相手先が停止中または到達不能）"; return; }
        if (!PathProbe.DirectoryExists(root)) { _vm.Message = $"ドライブ {letter}: は使えません"; return; }
        _vm.Active.Load(root);
        SyncTree();
    }

    // ---- P: 圧縮 / U: 解凍 -----------------------------------------------------

    private void CmdPack()
    {
        var items = _vm.Active.TargetEntries();
        if (items.Count == 0) return;
        var dlg = new ArchiveDialog(this, _vm.Settings, pack: true,
            $"{Describe(items)} を zip に圧縮します。", _vm.Active.Path, OppositePath,
            ArchiveService.DefaultZipName(items, _vm.Active.Path));
        if (dlg.ShowDialog() != true) return;

        var folder = dlg.Folder;
        var jobs = new List<(IReadOnlyList<FileEntry> items, string zip)>();
        if (dlg.EachObject)
            foreach (var it in items)
                jobs.Add((new[] { it }, Path.Combine(folder, (it.IsDirectory ? it.Name : Path.GetFileNameWithoutExtension(it.Name)) + ".zip")));
        else
            jobs.Add((items, Path.Combine(folder, dlg.ZipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? dlg.ZipName : dlg.ZipName + ".zip")));

        var existing = jobs.Where(j => File.Exists(j.zip)).Select(j => Path.GetFileName(j.zip)).ToList();
        if (existing.Count > 0 && !Confirm($"既に存在します。上書きしますか？\n{string.Join("\n", existing)}")) return;

        var level = dlg.Level;   // ダイアログの UI 要素は UI スレッドでしか読めないので、別スレッドの処理に渡す前に取り出す
        Try(() =>
        {
            Directory.CreateDirectory(folder);
            foreach (var (src, zip) in jobs)
            {
                var pw = new ProgressWindow(this, "ファイルの圧縮", $"{Path.GetFileName(zip)} を作成中...");
                bool ok = pw.Run((progress, token) => ArchiveService.Pack(src, zip, level, progress, token));
                if (pw.Error is not null) throw pw.Error;
                if (!ok) { _vm.Message = "圧縮を中止しました"; return; }
            }
            _vm.Message = $"圧縮: 完了 ({jobs.Count} 件)";
        }, "圧縮");
        _vm.Active.TagAll(false);
        _vm.ReloadBoth();
        if (dlg.GoAfter && Directory.Exists(folder)) { _vm.Active.Load(folder, Path.GetFileName(jobs[0].zip)); SyncTree(); }
    }

    private void CmdUnpack()
    {
        var items = _vm.Active.TargetEntries().Where(i => !i.IsDirectory && ArchiveService.IsArchive(i.FullPath)).ToList();
        if (items.Count == 0) { _vm.Message = "zip ファイルにカーソルを合わせる（またはタグを付ける）と解凍できます"; return; }
        var dlg = new ArchiveDialog(this, _vm.Settings, pack: false,
            $"{Describe(items)} を解凍します。", _vm.Active.Path, OppositePath, "");
        if (dlg.ShowDialog() != true) return;

        var folder = dlg.Folder;
        var overwrite = dlg.Overwrite;   // 同上（別スレッドから CheckBox を読むと InvalidOperationException）
        string? lastDest = null;
        Try(() =>
        {
            foreach (var zip in items)
            {
                var dest = dlg.MakeSubdir ? Path.Combine(folder, Path.GetFileNameWithoutExtension(zip.Name)) : folder;
                lastDest = dest;
                var pw = new ProgressWindow(this, "ファイルの解凍", $"{zip.Name} を展開中...");
                bool ok = pw.Run((progress, token) => ArchiveService.Unpack(zip.FullPath, dest, overwrite, progress, token));
                if (pw.Error is not null) throw pw.Error;
                if (!ok) { _vm.Message = "解凍を中止しました"; return; }
            }
            _vm.Message = $"解凍: 完了 ({items.Count} 件)";
        }, "解凍");
        _vm.Active.TagAll(false);
        _vm.ReloadBoth();
        if (dlg.GoAfter && lastDest is not null && Directory.Exists(lastDest)) { _vm.Active.Load(lastDest); SyncTree(); }
    }

    /// <summary>F: ファイルの検索。見つかったものを選ぶとその場所へ移動してカーソルを合わせる。</summary>
    private void CmdFind()
    {
        var w = new FindWindow(this, _vm.Settings, _vm.Active.Path);
        w.ShowDialog();
        if (w.SelectedPath is not { } p) return;
        if (Directory.Exists(p)) _vm.Active.Load(p);
        else _vm.Active.Load(Path.GetDirectoryName(p) ?? _vm.Active.Path, Path.GetFileName(p));
        SyncTree();
        _vm.Message = $"検索結果へ移動: {p}";
    }

    /// <summary>Shift+F: 文字列の検索（grep）。結果から移動 / ビューア / エディタ / タグ付け。</summary>
    private void CmdGrep()
    {
        var w = new GrepWindow(this, _vm.Settings, _vm.Active.Path);
        w.ShowDialog();
        if (w.TagPaths is { Count: > 0 } tags)
        {
            // ヒットしたファイルにタグ: まず最初のファイルのフォルダへ移動し、そのフォルダ内のヒットにタグを付ける
            var dir = Path.GetDirectoryName(tags[0]) ?? _vm.Active.Path;
            _vm.Active.Load(dir, Path.GetFileName(tags[0]));
            var set = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var e in _vm.Active.Entries) if (!e.IsParent && set.Contains(e.FullPath)) { e.Tagged = true; n++; }
            _vm.Active.NotifyCounts();
            SyncTree();
            int other = tags.Count - n;
            _vm.Message = $"検索でヒットした {n} 件にタグを付けました" + (other > 0 ? $"（他のフォルダの {other} 件は対象外）" : "");
            return;
        }
        if (w.SelectedPath is not { } p) return;
        _vm.Active.Load(Path.GetDirectoryName(p) ?? _vm.Active.Path, Path.GetFileName(p));
        SyncTree();
        _vm.Message = $"検索結果へ移動: {p}";
    }

    private void CmdJumpPath()
    {
        var p = InputDialog.Show(this, "移動先パス（\\\\server\\share、file://…、\"…\"、ファイルのパスも可）", _vm.Active.Path);
        if (string.IsNullOrWhiteSpace(p)) return;
        if (PathNormalizer.Resolve(p) is { } r) { _vm.Active.Load(r.Dir, r.SelectName); SyncTree(); }
        else _vm.Message = $"ディレクトリが見つかりません: {PathNormalizer.Normalize(p)}";
    }

    /// <summary>Ctrl+Shift+B: ファイル名（パスなし）だけをクリップボードへ。タグがあれば 1 行 1 件。</summary>
    private void CmdCopyName()
    {
        var items = _vm.Active.TargetEntries();
        if (items.Count == 0) return;
        FileOperations.SetClipboardText(string.Join(Environment.NewLine, items.Select(i => i.Name)));
        _vm.Message = items.Count == 1 ? $"ファイル名をコピーしました: {items[0].Name}" : $"{items.Count} 件のファイル名をコピーしました";
    }

    private void CmdCopyPath()
    {
        var items = _vm.Active.TargetEntries();
        if (items.Count == 0) return;
        FileOperations.SetClipboardText(string.Join(Environment.NewLine, items.Select(i => i.FullPath)));
        _vm.Message = $"{items.Count} 件のパスをクリップボードへコピーしました";
    }

    private void CmdSettings()
    {
        if (new SettingsWindow(this, _vm.Settings).ShowDialog() == true)
        {
            _vm.Left.ShowHidden = _vm.Right.ShowHidden = _vm.Settings.ShowHidden;
            _vm.Left.HiddenGrouping = _vm.Right.HiddenGrouping = MainViewModel.ParseGrouping(_vm.Settings.HiddenGrouping);
            ApplyListMode(_listModeIndex, announce: false);   // リストタブの変更を反映
            DiffToolService.Setting = _vm.Settings.DiffTool;
            _vm.Message = "設定を保存しました: " + Settings.FilePath;
        }
    }

    private void ShowHelp() => new HelpWindow(this).ShowDialog();

    // ---- 補助 --------------------------------------------------------------

    private static string Describe(IReadOnlyList<FileEntry> items)
        => items.Count == 1 ? $"「{items[0].Name}」" : $"{items.Count} 件";

    private bool Confirm(string text)
        => MessageBox.Show(this, text, "FDLike2026", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void Try(Action action, string what)
    {
        try
        {
            action();
            _vm.Message = $"{what}: 完了";
        }
        catch (OperationCanceledException)
        {
            _vm.Message = $"{what}: 中止";
        }
        catch (Exception ex)
        {
            // 起動中に .NET が更新された場合は、分かりにくい「ファイルが見つかりません」の代わりに再起動を案内する
            var msg = App.RuntimeUpdatedMessage(ex) ?? ex.Message;
            _vm.Message = $"{what}: エラー - {msg.Replace("\n", " ")}";
            App.Log(ex);   // スタックトレースを error.log に残す
            MessageBox.Show(this, msg, $"FDLike2026 - {what}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
