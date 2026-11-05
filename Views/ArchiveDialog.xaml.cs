using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Input;
using FDLike2026.Services;

namespace FDLike2026.Views;

/// <summary>P: ファイルの圧縮 / U: ファイルの解凍 の設定ダイアログ。</summary>
public partial class ArchiveDialog : Window
{
    private readonly Settings _settings;
    private readonly string _currentDir;
    private readonly string _otherDir;
    public bool IsPack { get; }

    public string Folder => FolderBox.Text.Trim().Trim('"');
    public string ZipName => ZipNameBox.Text.Trim();
    public bool EachObject => EachCheck.IsChecked == true;
    public bool MakeSubdir => SubdirCheck.IsChecked == true;
    public bool Overwrite => OverwriteCheck.IsChecked == true;
    public bool GoAfter => GoCheck.IsChecked == true;
    public CompressionLevel Level => LevelCombo.SelectedIndex switch
    {
        1 => CompressionLevel.Fastest,
        2 => CompressionLevel.SmallestSize,
        3 => CompressionLevel.NoCompression,
        _ => CompressionLevel.Optimal,
    };

    public ArchiveDialog(Window owner, Settings settings, bool pack, string targetDescription, string currentDir, string otherDir, string defaultZipName)
    {
        InitializeComponent();
        Owner = owner;
        _settings = settings;
        _currentDir = currentDir;
        _otherDir = otherDir;
        IsPack = pack;

        Title = pack ? "ファイルの圧縮" : "ファイルの解凍";
        TargetText.Text = targetDescription;
        PackPanel.Visibility = pack ? Visibility.Visible : Visibility.Collapsed;
        PackOptions.Visibility = pack ? Visibility.Visible : Visibility.Collapsed;
        UnpackOptions.Visibility = pack ? Visibility.Collapsed : Visibility.Visible;
        FolderLabel.Content = pack ? "書庫を作成するフォルダ(_F):" : "解凍先フォルダ(_F):";
        ZipNameBox.Text = defaultZipName;
        FolderBox.Text = currentDir;
        Loaded += (_, _) =>
        {
            if (pack) { ZipNameBox.Focus(); ZipNameBox.Select(0, Math.Max(0, defaultZipName.Length - 4)); }
            else FolderBox.Focus();
        };
    }

    private void Current_Click(object sender, RoutedEventArgs e) => FolderBox.Text = _currentDir;
    private void Other_Click(object sender, RoutedEventArgs e) => FolderBox.Text = _otherDir;

    private void Jump_Click(object sender, RoutedEventArgs e)
    {
        var w = new JumpWindow(this, _settings, Folder);
        w.ShowDialog();
        if (w.SelectedPath is { } p) FolderBox.Text = p;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = ((string)FolderLabel.Content).Replace("(_F):", ""),
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(Folder) ? Folder : _currentDir,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) FolderBox.Text = dlg.SelectedPath;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Folder.Length == 0) { MessageBox.Show(this, "フォルダを指定してください。", Title); return; }
        if (IsPack && ZipName.Length == 0) { MessageBox.Show(this, "圧縮ファイル名を指定してください。", Title); return; }
        if (IsPack && ZipName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { MessageBox.Show(this, "ファイル名に使えない文字が含まれています。", Title); return; }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (AccessKeys.TryHandle(this, e)) { e.Handled = true; return; }   // (_X) の X キー（Alt なし）
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }
}
