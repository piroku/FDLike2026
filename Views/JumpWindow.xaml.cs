using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FDLike2026.Services;

namespace FDLike2026.Views;

/// <summary>ジャンプ先 1 件。Key は "F1".."F12" / "A".."Z"。</summary>
public sealed class JumpSlot : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Key { get; }
    public string Label => Key.StartsWith('F') && Key.Length > 1 ? $"F{int.Parse(Key[1..]):00}" : Key;
    private string _path = "";
    public string Path
    {
        get => _path;
        set { _path = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Path))); }
    }
    public JumpSlot(string key, string path) { Key = key; _path = path; }
}

/// <summary>
/// WinFM2008 の JumpFolder 相当。F1～F12 と A～Z の計 38 スロットにパスを登録し、1 キーで移動する。
/// </summary>
public partial class JumpWindow : Window
{
    private readonly Settings _settings;
    private readonly string _currentDir;
    private readonly List<JumpSlot> _slots = new();

    /// <summary>ジャンプ先が選ばれたとき、そのパス。閉じただけなら null。</summary>
    public string? SelectedPath { get; private set; }
    /// <summary>パス欄にファイルのパスが貼られたとき、移動後にカーソルを合わせるファイル名。</summary>
    public string? SelectName { get; private set; }

    public static IEnumerable<string> AllKeys()
    {
        for (int i = 1; i <= 12; i++) yield return $"F{i}";
        for (char c = 'A'; c <= 'Z'; c++) yield return c.ToString();
    }

    public JumpWindow(Window owner, Settings settings, string currentDir)
    {
        InitializeComponent();
        Owner = owner;
        _settings = settings;
        _currentDir = currentDir;

        foreach (var k in AllKeys())
            _slots.Add(new JumpSlot(k, settings.JumpFolders.TryGetValue(k, out var p) ? p : ""));
        List.ItemsSource = _slots;
        List.SelectedIndex = 0;

        // クリップボードに「存在するパス」が入っていれば、パス欄にあらかじめ入れておく（移動はしない）
        Loaded += (_, _) =>
        {
            try
            {
                if (Clipboard.ContainsText() && PathNormalizer.Resolve(Clipboard.GetText()) is { } r)
                {
                    PathBox.Text = PathNormalizer.Normalize(Clipboard.GetText());
                    PathBox.Foreground = System.Windows.Media.Brushes.Yellow;   // 「貼り付け済み」の合図
                }
            }
            catch { }
            Focus();
        };
        PathBox.TextChanged += (_, _) => PathBox.Foreground = System.Windows.Media.Brushes.White;
    }

    private JumpSlot? Current => List.SelectedItem as JumpSlot;

    /// <summary>パス欄の内容へ 1 回だけ移動（JumpFolder には登録しない）。</summary>
    private void GoPath()
    {
        var r = PathNormalizer.Resolve(PathBox.Text);
        if (r is null)
        {
            MessageBox.Show(this, $"フォルダが見つかりません:\n{PathNormalizer.Normalize(PathBox.Text)}", "JumpFolder", MessageBoxButton.OK, MessageBoxImage.Warning);
            PathBox.Focus(); PathBox.SelectAll();
            return;
        }
        SelectedPath = r.Dir;
        SelectName = r.SelectName;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;

        // ---- パス欄に入力中 ----
        if (PathBox.IsKeyboardFocused)
        {
            switch (key)
            {
                case Key.Enter: e.Handled = true; GoPath(); return;
                case Key.Escape:
                    e.Handled = true;
                    if (PathBox.Text.Length > 0) { PathBox.Clear(); Focus(); } else Close();
                    return;
                case Key.Tab:
                case Key.Up:
                case Key.Down: e.Handled = true; Focus(); return;   // 一覧へ戻る
            }
            return;   // それ以外の文字はテキストボックスへ
        }

        // Tab / Ctrl+V: パス欄へ（Ctrl+V はクリップボードを入れて、そのまま移動できれば移動）
        if (key == Key.Tab) { e.Handled = true; PathBox.Focus(); PathBox.SelectAll(); return; }
        if (key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            try
            {
                if (Clipboard.ContainsText())
                {
                    PathBox.Text = PathNormalizer.Normalize(Clipboard.GetText());
                    if (PathNormalizer.Resolve(PathBox.Text) is not null) { GoPath(); return; }
                }
            }
            catch { }
            PathBox.Focus(); PathBox.CaretIndex = PathBox.Text.Length;
            return;
        }

        e.Handled = true;
        switch (key)
        {
            case Key.Escape: Close(); return;
            case Key.Up: Move(-1); return;
            case Key.Down: Move(1); return;
            case Key.PageUp: Move(-10); return;
            case Key.PageDown: Move(10); return;
            case Key.Home: List.SelectedIndex = 0; List.ScrollIntoView(Current); return;
            case Key.End: List.SelectedIndex = _slots.Count - 1; List.ScrollIntoView(Current); return;
            case Key.Enter: JumpTo(Current); return;
            case Key.Space:
            case Key.Insert: Register(Current, _currentDir); return;
            case Key.Delete: Register(Current, ""); return;
        }

        // 1 キーでジャンプ: F1～F12 / A～Z（A～Z は全部ジャンプに使うので、パス編集は Shift+E）
        string? slotKey = null;
        if (key >= Key.F1 && key <= Key.F12) slotKey = $"F{key - Key.F1 + 1}";
        else if (key >= Key.A && key <= Key.Z && Keyboard.Modifiers == ModifierKeys.None) slotKey = ((char)('A' + (key - Key.A))).ToString();
        else if (key == Key.E && Keyboard.Modifiers == ModifierKeys.Shift && Current is { } c2) { Edit(c2); return; }

        if (slotKey is not null)
        {
            var slot = _slots.First(s => s.Key == slotKey);
            List.SelectedItem = slot;
            List.ScrollIntoView(slot);
            if (string.IsNullOrEmpty(slot.Path))
                Register(slot, _currentDir);     // 未登録のキーを押したら、その場で現在のディレクトリを登録（WinFM 流）
            else
                JumpTo(slot);
            return;
        }
        e.Handled = false;
    }

    private void Move(int delta)
    {
        List.SelectedIndex = Math.Clamp(List.SelectedIndex + delta, 0, _slots.Count - 1);
        List.ScrollIntoView(Current);
    }

    private void JumpTo(JumpSlot? slot)
    {
        if (slot is null || string.IsNullOrWhiteSpace(slot.Path)) return;
        var p = Environment.ExpandEnvironmentVariables(slot.Path);
        if (!Directory.Exists(p))
        {
            MessageBox.Show(this, $"ディレクトリが見つかりません:\n{p}", "JumpFolder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SelectedPath = p;
        Close();
    }

    private void Register(JumpSlot? slot, string path)
    {
        if (slot is null) return;
        slot.Path = path;
        if (string.IsNullOrEmpty(path)) _settings.JumpFolders.Remove(slot.Key);
        else _settings.JumpFolders[slot.Key] = path;
        _settings.Save();
    }

    private void Edit(JumpSlot slot)
    {
        var p = InputDialog.Show(this, $"{slot.Label} に登録するパス（空にすると解除）", slot.Path);
        if (p is null) return;
        Register(slot, p.Trim().Trim('"'));
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) => JumpTo(Current);

    // ---- マウス ----
    private JumpSlot? RowAt(Point pt)
    {
        var hit = List.InputHitTest(pt) as DependencyObject;
        while (hit is not null && hit is not System.Windows.Controls.ListBoxItem) hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        return (hit as System.Windows.Controls.ListBoxItem)?.DataContext as JumpSlot;
    }

    private JumpSlot? _pressed;

    /// <summary>押した時点でカーソル（選択）をその行へ。ListBoxItem を Focusable=False にしているので自前で選ぶ。</summary>
    private void List_MouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = RowAt(e.GetPosition(List));
        if (_pressed is not null)
        {
            List.SelectedItem = _pressed;
            List.ScrollIntoView(_pressed);
            List.UpdateLayout();   // 選択の反転を描く
            // ダブルクリックで登録済みの行へジャンプ（Preview を握っているので自前で判定）
            if (e.ClickCount >= 2 && !string.IsNullOrWhiteSpace(_pressed.Path)) { e.Handled = true; JumpTo(_pressed); return; }
        }
        e.Handled = true;          // ListBox 側の選択処理（フォーカス移動）は使わない
    }

    /// <summary>シングルクリックは選択だけ。ジャンプはダブルクリック / Enter / キー文字で。</summary>
    private void List_MouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        _pressed = null;
        e.Handled = true;
    }

    /// <summary>右クリックした行を選択してからコンテキストメニューを出す。</summary>
    private void List_MouseRightDown(object sender, MouseButtonEventArgs e)
    {
        if (RowAt(e.GetPosition(List)) is { } slot) List.SelectedItem = slot;
    }

    private void Menu_Jump(object sender, RoutedEventArgs e) => JumpTo(Current);
    private void Menu_Register(object sender, RoutedEventArgs e) => Register(Current, _currentDir);
    private void Menu_Delete(object sender, RoutedEventArgs e) => Register(Current, "");
    private void Menu_Edit(object sender, RoutedEventArgs e) { if (Current is { } c) Edit(c); }
}
