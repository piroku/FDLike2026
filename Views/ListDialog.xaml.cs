using System.Windows;
using System.Windows.Input;

namespace FDLike2026.Views;

public sealed record ListChoice(string Key, string Label, object? Value);

/// <summary>装置選択・ソート選択などに使う、1 文字キーで選べるリストダイアログ。</summary>
public partial class ListDialog : Window
{
    private readonly IReadOnlyList<ListChoice> _choices;
    public ListChoice? Selected { get; private set; }

    public ListDialog(Window owner, string prompt, IReadOnlyList<ListChoice> choices, int initialIndex = 0)
    {
        InitializeComponent();
        Owner = owner;
        PromptText.Text = prompt;
        _choices = choices;
        List.ItemsSource = choices;
        List.SelectedIndex = Math.Clamp(initialIndex, 0, Math.Max(0, choices.Count - 1));
        Loaded += (_, _) => List.Focus();
    }

    private void Accept()
    {
        Selected = List.SelectedItem as ListChoice;
        DialogResult = Selected is not null;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; return; }
        if (e.Key == Key.Enter) { Accept(); e.Handled = true; return; }
        var ch = KeyToChar(e.Key);
        if (ch is null) return;
        var hit = _choices.FirstOrDefault(c => string.Equals(c.Key, ch, StringComparison.OrdinalIgnoreCase));
        if (hit is not null)
        {
            List.SelectedItem = hit;
            Accept();
            e.Handled = true;
        }
    }

    private static string? KeyToChar(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return ((char)('A' + (k - Key.A))).ToString();
        if (k >= Key.D0 && k <= Key.D9) return ((char)('0' + (k - Key.D0))).ToString();
        return null;
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

    public static ListChoice? Show(Window owner, string prompt, IReadOnlyList<ListChoice> choices, int initialIndex = 0)
    {
        var d = new ListDialog(owner, prompt, choices, initialIndex);
        return d.ShowDialog() == true ? d.Selected : null;
    }
}
