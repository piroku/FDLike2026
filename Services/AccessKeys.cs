using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FDLike2026.Services;

/// <summary>
/// WinFM 風のショートカット: ボタン / チェック / ラジオの「(_X)」を、Alt なしの X キーでも押せるようにする。
/// 文字を入力中（TextBox / 編集可能 ComboBox にフォーカス）のときは何もしない（Alt+X は WPF 標準で効く）。
/// 各ダイアログの PreviewKeyDown から呼ぶ。
/// </summary>
public static class AccessKeys
{
    public static bool TryHandle(DependencyObject root, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control) || mods.HasFlag(ModifierKeys.Alt)) return false;
        if (IsTyping()) return false;

        char ch = KeyToChar(e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key);
        if (ch == '\0') return false;
        var marker = "(_" + ch + ")";

        foreach (var ctl in Descendants(root))
        {
            if (ctl is not ContentControl cc || cc is Window) continue;
            if (!cc.IsVisible || !cc.IsEnabled) continue;
            if (cc.Content is not string text) continue;
            if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0) continue;

            switch (cc)
            {
                case CheckBox cb: cb.IsChecked = cb.IsChecked != true; cb.Focus(); return true;
                case RadioButton rb: rb.IsChecked = true; rb.Focus(); return true;
                case Button b:
                    b.Focus();
                    b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    return true;
                case Label lab:
                    if (lab.Target is { } t) { t.Focus(); if (t is TextBox tb) tb.SelectAll(); return true; }
                    break;
            }
        }
        return false;
    }

    private static bool IsTyping()
    {
        var f = Keyboard.FocusedElement as DependencyObject;
        while (f is not null)
        {
            if (f is TextBoxBase or PasswordBox) return true;
            if (f is ComboBox { IsEditable: true }) return true;
            f = f is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(f) : LogicalTreeHelper.GetParent(f);
        }
        return false;
    }

    private static char KeyToChar(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return (char)('A' + (k - Key.A));
        if (k >= Key.D0 && k <= Key.D9) return (char)('0' + (k - Key.D0));
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return (char)('0' + (k - Key.NumPad0));
        return '\0';
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            yield return d;
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = n - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(d, i));
        }
    }
}
