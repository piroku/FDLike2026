using System.IO;
using System.Windows;
using System.Windows.Input;
using FDLike2026.Models;
using FDLike2026.Services;

namespace FDLike2026.Views;

/// <summary>
/// 同じフォルダへ複写・移動するときの「名前の変更」（WinFM2008 準拠）。
/// 名前 / 場所 / 種類 を示し、新しい名前を入力。OK(Y) / スキップ(S) / キャンセル(C)。
/// </summary>
public partial class RenameCopyDialog : Window
{
    public enum Choice { Ok, Skip, Cancel }
    public Choice Result { get; private set; } = Choice.Cancel;
    public string NewNameText => NewName.Text.Trim();

    private readonly FileEntry _item;

    public RenameCopyDialog(Window owner, FileEntry item, bool move)
    {
        InitializeComponent();
        Owner = owner;
        _item = item;
        Lead.Text = move ? "同じフォルダへ移動します（名前の変更になります）。新しい名前を付けてください。"
                         : "同じフォルダへ複写します。新しい名前を付けてください。";
        NameText.Text = item.Name;
        PlaceText.Text = Path.GetDirectoryName(item.FullPath) ?? "";
        KindText.Text = item.IsDirectory ? "ファイル フォルダー" : KindOf(item.Name);
        NewName.Text = SuggestName(item);
        Loaded += (_, _) =>
        {
            NewName.Focus();
            // 拡張子を除いた部分だけ選択しておく（上書き入力しやすいように）
            int dot = item.IsDirectory ? -1 : NewName.Text.LastIndexOf('.');
            NewName.Select(0, dot > 0 ? dot : NewName.Text.Length);
        };
    }

    private static string KindOf(string name)
    {
        var ext = Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
        return ext.Length == 0 ? "ファイル" : $"{ext} ファイル";
    }

    /// <summary>「名前 - コピー」「名前 - コピー (2)」のように、まだ存在しない名前を提案する。</summary>
    private static string SuggestName(FileEntry item)
    {
        var dir = Path.GetDirectoryName(item.FullPath) ?? "";
        var stem = item.IsDirectory ? item.Name : Path.GetFileNameWithoutExtension(item.Name);
        var ext = item.IsDirectory ? "" : Path.GetExtension(item.Name);
        for (int i = 1; i < 1000; i++)
        {
            var cand = i == 1 ? $"{stem} - コピー{ext}" : $"{stem} - コピー ({i}){ext}";
            var full = Path.Combine(dir, cand);
            if (!File.Exists(full) && !Directory.Exists(full)) return cand;
        }
        return item.Name;
    }

    private void Ok()
    {
        var n = NewNameText;
        if (n.Length == 0 || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(this, "名前に使えない文字が含まれているか、空です。", "名前の変更", MessageBoxButton.OK, MessageBoxImage.Warning);
            NewName.Focus(); return;
        }
        if (string.Equals(n, _item.Name, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "元と同じ名前です。別の名前を付けるか、スキップしてください。", "名前の変更", MessageBoxButton.OK, MessageBoxImage.Information);
            NewName.Focus(); NewName.SelectAll(); return;
        }
        var full = Path.Combine(Path.GetDirectoryName(_item.FullPath) ?? "", n);
        if (File.Exists(full) || Directory.Exists(full))
        {
            if (MessageBox.Show(this, $"{n} は既にあります。上書きの確認に進みますか？", "名前の変更", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            { NewName.Focus(); NewName.SelectAll(); return; }
        }
        Result = Choice.Ok; DialogResult = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Ok();
    private void Skip_Click(object sender, RoutedEventArgs e) { Result = Choice.Skip; DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { Result = Choice.Cancel; DialogResult = false; }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Result = Choice.Cancel; DialogResult = false; e.Handled = true; return; }
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { Ok(); e.Handled = true; return; }
        if (AccessKeys.TryHandle(this, e)) { e.Handled = true; return; }   // Y / S / C（入力中は Alt+）
    }
}
