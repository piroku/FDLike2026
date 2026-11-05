using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FDLike2026.Views;

public partial class AttributeDialog : Window
{
    public FileAttributes SetFlags { get; private set; }
    public FileAttributes ClearFlags { get; private set; }

    public AttributeDialog(Window owner, string prompt, FileAttributes? current)
    {
        InitializeComponent();
        Owner = owner;
        PromptText.Text = prompt;
        // 複数対象のときは current = null → 「変更しない」(null) で開始
        ChkR.IsChecked = current?.HasFlag(FileAttributes.ReadOnly);
        ChkH.IsChecked = current?.HasFlag(FileAttributes.Hidden);
        ChkS.IsChecked = current?.HasFlag(FileAttributes.System);
        ChkA.IsChecked = current?.HasFlag(FileAttributes.Archive);
        Loaded += (_, _) => ChkR.Focus();
    }

    private static void Cycle(CheckBox c) => c.IsChecked = c.IsChecked switch { true => false, false => null, null => true };

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: DialogResult = false; e.Handled = true; break;
            case Key.Enter:
                Collect(ChkR, FileAttributes.ReadOnly);
                Collect(ChkH, FileAttributes.Hidden);
                Collect(ChkS, FileAttributes.System);
                Collect(ChkA, FileAttributes.Archive);
                DialogResult = true; e.Handled = true; break;
            case Key.R: Cycle(ChkR); e.Handled = true; break;
            case Key.H: Cycle(ChkH); e.Handled = true; break;
            case Key.S: Cycle(ChkS); e.Handled = true; break;
            case Key.A: Cycle(ChkA); e.Handled = true; break;
        }
    }

    private void Collect(CheckBox c, FileAttributes flag)
    {
        if (c.IsChecked == true) SetFlags |= flag;
        else if (c.IsChecked == false) ClearFlags |= flag;
    }
}
