using System.IO;
using System.Windows;
using System.Windows.Input;
using FDLike2026.Models;
using FDLike2026.Services;

namespace FDLike2026.Views;

/// <summary>C / M: ファイルの複写・移動ダイアログ（WinFM2008 準拠）。</summary>
public partial class CopyMoveDialog : Window
{
    private readonly Settings _settings;
    private readonly string _currentDir, _otherDir;

    public CopyOptions Options { get; } = new();
    public bool GoAfter => GoCheck.IsChecked == true;

    public CopyMoveDialog(Window owner, Settings settings, bool move, IReadOnlyList<FileEntry> items, string currentDir, string otherDir)
    {
        InitializeComponent();
        Owner = owner;
        _settings = settings;
        _currentDir = currentDir;
        _otherDir = otherDir;
        Options.Move = move;

        Title = move ? "ファイルの移動" : "ファイルの複写";
        DestLabel.Content = move ? "移動先フォルダ(_D):" : "複写先フォルダ(_D):";
        OkButton.Content = move ? "移動 (Enter)" : "複写 (Enter)";
        TargetText.Text = items.Count == 1
            ? $"「{items[0].Name}」を{(move ? "移動" : "複写")}します。"
            : $"{items.Count} 件（フォルダ {items.Count(i => i.IsDirectory)} / ファイル {items.Count(i => !i.IsDirectory)}）を{(move ? "移動" : "複写")}します。";

        // 宛先の履歴（反対側を先頭に）
        var hist = new List<string> { otherDir };
        hist.AddRange(settings.CopyHistory.Where(h => !string.Equals(h, otherDir, StringComparison.OrdinalIgnoreCase)));
        DestBox.ItemsSource = hist;
        DestBox.Text = otherDir;

        RenamePanel.Visibility = items.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
        RenameBox.Text = items.Count == 1 ? items[0].Name : "";
        Loaded += (_, _) => { DestBox.Focus(); };
    }

    public string Dest => (DestBox.Text ?? "").Trim().Trim('"');

    private void Jump_Click(object sender, RoutedEventArgs e)
    {
        var w = new JumpWindow(this, _settings, Dest.Length > 0 ? Dest : _currentDir);
        w.ShowDialog();
        if (w.SelectedPath is { } p) DestBox.Text = p;
    }
    private void Other_Click(object sender, RoutedEventArgs e) => DestBox.Text = _otherDir;
    private void Current_Click(object sender, RoutedEventArgs e) => DestBox.Text = _currentDir;
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = ((string)DestLabel.Content).Replace("(_D):", ""),
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(Dest) ? Dest : _currentDir,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) DestBox.Text = dlg.SelectedPath;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var dest = Environment.ExpandEnvironmentVariables(Dest);
        if (dest.Length == 0) { MessageBox.Show(this, "宛先フォルダを指定してください。", Title); return; }
        if (!Directory.Exists(dest))
        {
            if (MessageBox.Show(this, $"フォルダがありません。作成しますか？\n{dest}", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try { Directory.CreateDirectory(dest); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); return; }
        }
        Options.DestDir = Path.GetFullPath(dest);
        Options.Overwrite = OwNewer.IsChecked == true ? OverwriteMode.Newer
                          : OwOverwrite.IsChecked == true ? OverwriteMode.Overwrite
                          : OwRename.IsChecked == true ? OverwriteMode.RenameNew
                          : OwSkip.IsChecked == true ? OverwriteMode.Skip
                          : OverwriteMode.Ask;
        Options.UpdateOnly = UpdateCheck.IsChecked == true;
        Options.ClearReadOnly = ReadOnlyCheck.IsChecked == true;
        Options.StructureOnly = StructureCheck.IsChecked == true;
        Options.IncludeFilter = IncludeBox.Text;
        Options.ExcludeFilter = ExcludeBox.Text;
        Options.RenameTo = RenamePanel.Visibility == Visibility.Visible && RenameCheck.IsChecked == true ? RenameBox.Text.Trim() : null;

        _settings.CopyHistory.RemoveAll(h => string.Equals(h, Options.DestDir, StringComparison.OrdinalIgnoreCase));
        _settings.CopyHistory.Insert(0, Options.DestDir);
        if (_settings.CopyHistory.Count > 20) _settings.CopyHistory.RemoveRange(20, _settings.CopyHistory.Count - 20);
        _settings.Save();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (AccessKeys.TryHandle(this, e)) { e.Handled = true; return; }   // (_X) の X キー（Alt なし）
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }
}
