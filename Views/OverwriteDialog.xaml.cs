using System.IO;
using System.Windows;
using System.Windows.Input;
using FDLike2026.Services;

namespace FDLike2026.Views;

/// <summary>複写・移動で同名ファイルがあったときの方法選択（WinFM2008 準拠）。</summary>
public partial class OverwriteDialog : Window
{
    public OverwriteMode Result { get; private set; } = OverwriteMode.Cancel;
    public bool ApplyAll => AllCheck.IsChecked == true;

    public OverwriteDialog(Window owner, string src, string dst)
    {
        InitializeComponent();
        Owner = owner;
        SrcText.Text = Describe(src);
        DstText.Text = Describe(dst);
    }

    private static string Describe(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return $"{path}\n{fi.Length:#,0} バイト　{fi.LastWriteTime:yyyy/MM/dd HH:mm:ss}";
        }
        catch { return path; }
    }

    private void Done(OverwriteMode m) { Result = m; DialogResult = true; }
    private void B_Overwrite(object sender, RoutedEventArgs e) => Done(OverwriteMode.Overwrite);
    private void B_Newer(object sender, RoutedEventArgs e) => Done(OverwriteMode.Newer);
    private void B_Rename(object sender, RoutedEventArgs e) => Done(OverwriteMode.RenameNew);
    private void B_Skip(object sender, RoutedEventArgs e) => Done(OverwriteMode.Skip);
    private void B_Cancel(object sender, RoutedEventArgs e) => Done(OverwriteMode.Cancel);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Done(OverwriteMode.Cancel); break;
            case Key.O: Done(OverwriteMode.Overwrite); break;
            case Key.N: Done(OverwriteMode.Newer); break;
            case Key.R: Done(OverwriteMode.RenameNew); break;
            case Key.S: Done(OverwriteMode.Skip); break;
            case Key.C: Done(OverwriteMode.Cancel); break;
            case Key.A: AllCheck.IsChecked = AllCheck.IsChecked != true; break;
            default: return;
        }
        e.Handled = true;
    }
}
