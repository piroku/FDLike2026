using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FDLike2026.Services;

namespace FDLike2026.Views;

public sealed class MacroSlot : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Key { get; }
    private string _line = "";
    /// <summary>設定行そのもの（「タイトル,コマンド」）。</summary>
    public string Line
    {
        get => _line;
        set
        {
            _line = value;
            foreach (var n in new[] { nameof(Line), nameof(Title), nameof(Command) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
    public string Title => MacroExpander.SplitTitle(_line).Title;
    public string Command => MacroExpander.SplitTitle(_line).Command;
    public MacroSlot(string key, string line) { Key = key; _line = line; }
}

/// <summary>F2: コマンドマクロ（WinFM2008 準拠）。A～Z / 1～9 のスロットにコマンドを登録し、1 キーで実行。</summary>
public partial class MacroWindow : Window
{
    private readonly Settings _settings;
    private readonly string _currentDir;
    private readonly List<MacroSlot> _slots = new();

    /// <summary>実行が選ばれたマクロ（展開前）。閉じただけなら null。</summary>
    public string? SelectedMacro { get; private set; }

    public static IEnumerable<string> AllKeys()
    {
        for (char c = 'A'; c <= 'Z'; c++) yield return c.ToString();
        for (char c = '1'; c <= '9'; c++) yield return c.ToString();
    }

    public MacroWindow(Window owner, Settings settings, string currentDir = "")
    {
        InitializeComponent();
        Owner = owner;
        _settings = settings;
        _currentDir = currentDir;
        foreach (var k in AllKeys())
            _slots.Add(new MacroSlot(k, settings.Macros.TryGetValue(k, out var l) ? l : ""));
        List.ItemsSource = _slots;
        List.SelectedIndex = 0;
    }

    private MacroSlot? Current => List.SelectedItem as MacroSlot;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        e.Handled = true;
        switch (key)
        {
            case Key.Escape: Close(); return;
            case Key.Up: Move(-1); return;
            case Key.Down: Move(1); return;
            case Key.PageUp: Move(-10); return;
            case Key.PageDown: Move(10); return;
            case Key.Home: List.SelectedIndex = 0; return;
            case Key.End: List.SelectedIndex = _slots.Count - 1; return;
            case Key.Enter: RunSlot(Current); return;
            case Key.Insert:
            case Key.Space: Edit(Current); return;
            case Key.Delete: SetLine(Current, ""); return;
        }
        string? slotKey = null;
        if (key >= Key.A && key <= Key.Z && Keyboard.Modifiers == ModifierKeys.None) slotKey = ((char)('A' + (key - Key.A))).ToString();
        else if (key >= Key.D1 && key <= Key.D9) slotKey = ((char)('1' + (key - Key.D1))).ToString();
        if (slotKey is not null)
        {
            var slot = _slots.First(s => s.Key == slotKey);
            List.SelectedItem = slot;
            List.ScrollIntoView(slot);
            if (string.IsNullOrEmpty(slot.Line)) Edit(slot); else RunSlot(slot);
            return;
        }
        e.Handled = false;
    }

    private void Move(int d) { List.SelectedIndex = Math.Clamp(List.SelectedIndex + d, 0, _slots.Count - 1); List.ScrollIntoView(Current); }

    private void RunSlot(MacroSlot? slot)
    {
        if (slot is null || string.IsNullOrWhiteSpace(slot.Command)) return;
        SelectedMacro = slot.Command;
        Close();
    }

    private void SetLine(MacroSlot? slot, string line)
    {
        if (slot is null) return;
        slot.Line = line;
        if (string.IsNullOrEmpty(line)) _settings.Macros.Remove(slot.Key);
        else _settings.Macros[slot.Key] = line;
        _settings.Save();
    }

    private void Edit(MacroSlot? slot)
    {
        if (slot is null) return;
        var line = MacroEditDialog.Show(this, slot.Key, slot.Line, _currentDir);
        if (line is null) return;
        SetLine(slot, line.Trim());
    }

    // ---- マウス ----
    private MacroSlot? RowAt(Point pt)
    {
        var hit = List.InputHitTest(pt) as DependencyObject;
        while (hit is not null && hit is not ListBoxItem) hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        return (hit as ListBoxItem)?.DataContext as MacroSlot;
    }
    private void List_MouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (RowAt(e.GetPosition(List)) is not { } slot) return;
        List.SelectedItem = slot;
        if (!string.IsNullOrWhiteSpace(slot.Command)) RunSlot(slot);
    }
    private void List_MouseRightDown(object sender, MouseButtonEventArgs e)
    {
        if (RowAt(e.GetPosition(List)) is { } slot) List.SelectedItem = slot;
    }
    private void Menu_Run(object sender, RoutedEventArgs e) => RunSlot(Current);
    private void Menu_Edit(object sender, RoutedEventArgs e) => Edit(Current);
    private void Menu_Delete(object sender, RoutedEventArgs e) => SetLine(Current, "");
}
