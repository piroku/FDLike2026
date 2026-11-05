using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using FDLike2026.Services;

namespace FDLike2026.Views;

public sealed record AssocRow(string Ext, string Command);

/// <summary>F4: 拡張子の対応（WinFM2008 準拠）。拡張子 → プログラム / マクロ。</summary>
public partial class AssocWindow : Window
{
    private readonly Settings _settings;
    private readonly ObservableCollection<AssocRow> _rows = new();

    public AssocWindow(Window owner, Settings settings)
    {
        InitializeComponent();
        Owner = owner;
        _settings = settings;
        foreach (var a in settings.Associations)
        {
            var (ext, cmd) = Split(a);
            if (ext.Length > 0) _rows.Add(new AssocRow(ext, cmd));
        }
        List.ItemsSource = _rows;
        if (_rows.Count > 0) List.SelectedIndex = 0;
    }

    public static (string Ext, string Command) Split(string line)
    {
        int i = line.IndexOf(',');
        return i < 0 ? (line.Trim(), "") : (line[..i].Trim().TrimStart('.'), line[(i + 1)..].Trim());
    }

    private void Save()
    {
        _settings.Associations = _rows.Select(r => $"{r.Ext},{r.Command}").ToList();
        _settings.Save();
    }

    private void Add_Click(object sender, RoutedEventArgs e) => EditRow(null);
    private void Edit_Click(object sender, RoutedEventArgs e) => EditRow(List.SelectedItem as AssocRow);
    private void List_DoubleClick(object sender, MouseButtonEventArgs e) => EditRow(List.SelectedItem as AssocRow);

    private void EditRow(AssocRow? row)
    {
        var ext = InputDialog.Show(this, "拡張子（ピリオドなし、ワイルドカード可。複数は ; 区切り: txt;md;log）", row?.Ext ?? "");
        if (string.IsNullOrWhiteSpace(ext)) return;
        ext = ext.Trim().TrimStart('.');
        var cmd = InputDialog.Show(this, $"「{ext}」を開くプログラム / マクロ（例: \"C:\\\\Program Files\\\\Hidemaru\\\\Hidemaru.exe\" $Q$P\\\\$C$Q）", row?.Command ?? "");
        if (cmd is null) return;
        var newRow = new AssocRow(ext, cmd.Trim());
        if (row is null) _rows.Add(newRow);
        else _rows[_rows.IndexOf(row)] = newRow;
        List.SelectedItem = newRow;
        Save();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not AssocRow row) return;
        int idx = _rows.IndexOf(row);
        _rows.Remove(row);
        if (_rows.Count > 0) List.SelectedIndex = Math.Min(idx, _rows.Count - 1);
        Save();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (AccessKeys.TryHandle(this, e)) { e.Handled = true; return; }   // (_X) の X キー（Alt なし）
        switch (e.Key)
        {
            case Key.Escape: Close(); e.Handled = true; break;
            case Key.Insert: EditRow(null); e.Handled = true; break;
            case Key.Enter: EditRow(List.SelectedItem as AssocRow); e.Handled = true; break;
            case Key.Delete: Delete_Click(sender, e); e.Handled = true; break;
            case Key.Up: if (List.SelectedIndex > 0) List.SelectedIndex--; e.Handled = true; break;
            case Key.Down: if (List.SelectedIndex < _rows.Count - 1) List.SelectedIndex++; e.Handled = true; break;
        }
    }

    /// <summary>ファイル名に対応するコマンドを探す。無ければ null。</summary>
    public static string? FindCommand(Settings settings, string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName).TrimStart('.');
        foreach (var a in settings.Associations)
        {
            var (pattern, cmd) = Split(a);
            if (cmd.Length == 0) continue;
            foreach (var p in pattern.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var rx = "^" + System.Text.RegularExpressions.Regex.Escape(p.Trim().TrimStart('.')).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                if (System.Text.RegularExpressions.Regex.IsMatch(ext, rx, System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return cmd;
            }
        }
        return null;
    }
}
