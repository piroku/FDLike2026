using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FDLike2026.ViewModels;

namespace FDLike2026.Views;

/// <summary>S: オブジェクトのソート（WinFM2008 のダイアログ構成）。キーを選び、昇順 / 降順で決定。</summary>
public partial class SortDialog : Window
{
    public SortMode Mode { get; private set; }
    public bool Descending { get; private set; }

    private ToggleButton[] Keys => new[] { KeyName, KeyExt, KeyDate, KeySize, KeyNone };

    public SortDialog(Window owner, SortMode current, bool descending)
    {
        InitializeComponent();
        Owner = owner;
        Mode = current;
        Descending = descending;
        Select(current);
        Loaded += (_, _) => (Button(current)).Focus();
    }

    private ToggleButton Button(SortMode m) => m switch
    {
        SortMode.Extension => KeyExt,
        SortMode.Date => KeyDate,
        SortMode.Size => KeySize,
        SortMode.None => KeyNone,
        _ => KeyName,
    };

    private void Select(SortMode m)
    {
        Mode = m;
        foreach (var b in Keys) b.IsChecked = ReferenceEquals(b, Button(m));
        bool none = m == SortMode.None;
        AscButton.IsEnabled = DescButton.IsEnabled = true;
        DescButton.IsEnabled = !none;
    }

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        if (sender == KeyName) Select(SortMode.Name);
        else if (sender == KeyExt) Select(SortMode.Extension);
        else if (sender == KeyDate) Select(SortMode.Date);
        else if (sender == KeySize) Select(SortMode.Size);
        else Select(SortMode.None);
    }

    private void Asc_Click(object sender, RoutedEventArgs e) => Finish(false);
    private void Desc_Click(object sender, RoutedEventArgs e) => Finish(Mode != SortMode.None);
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Finish(bool desc)
    {
        Descending = desc;
        DialogResult = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Escape: DialogResult = false; break;
            case Key.N: Select(SortMode.Name); break;
            case Key.E: Select(SortMode.Extension); break;
            case Key.D: Select(SortMode.Date); break;
            case Key.S: Select(SortMode.Size); break;
            case Key.X: Select(SortMode.None); break;
            case Key.U: Finish(false); break;
            case Key.R: Finish(Mode != SortMode.None); break;
            case Key.Enter: Finish(shift && Mode != SortMode.None); break;
            case Key.Up: MoveFocus(-1); break;
            case Key.Down: MoveFocus(1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void MoveFocus(int delta)
    {
        var keys = Keys;
        int i = Array.FindIndex(keys, k => k.IsChecked == true);
        i = Math.Clamp(i + delta, 0, keys.Length - 1);
        Key_Click(keys[i], new RoutedEventArgs());
        keys[i].Focus();
    }
}
