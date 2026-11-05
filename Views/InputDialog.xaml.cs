using System.Windows;
using System.Windows.Input;

namespace FDLike2026.Views;

public partial class InputDialog : Window
{
    public string Value => Input.Text;

    public InputDialog(Window owner, string prompt, string initial = "", bool selectBaseNameOnly = false, string? title = null)
    {
        InitializeComponent();
        Owner = owner;
        if (title is not null) Title = title;
        PromptText.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) =>
        {
            Input.Focus();
            if (selectBaseNameOnly)
            {
                int dot = initial.LastIndexOf('.');
                Input.Select(0, dot > 0 ? dot : initial.Length);
            }
            else Input.SelectAll();
        };
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
        else if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }

    public static string? Show(Window owner, string prompt, string initial = "", bool selectBaseNameOnly = false, string? title = null)
    {
        var d = new InputDialog(owner, prompt, initial, selectBaseNameOnly, title);
        return d.ShowDialog() == true ? d.Value : null;
    }
}
