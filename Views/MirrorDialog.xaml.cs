using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FDLike2026.Services;

namespace FDLike2026.Views;

/// <summary>保存できるミラーコピーの手順。</summary>
public sealed class MirrorProc
{
    public string Src { get; set; } = "";
    public string Dst { get; set; } = "";
    public string Include { get; set; } = "*.*";
    public string Exclude { get; set; } = "";
    public string ExcludeDir { get; set; } = "";
    public bool Confirm { get; set; } = true;
    public bool Delete { get; set; } = true;
    public bool Hidden { get; set; } = true;
}

/// <summary>Ctrl+M: ミラーコピー（robocopy /MIR のフロントエンド）。</summary>
public partial class MirrorDialog : Window
{
    private readonly Settings _settings;
    private readonly string _currentDir, _otherDir;
    private bool _loading;

    public MirrorProc Proc { get; private set; } = new();

    public MirrorDialog(Window owner, Settings settings, string currentDir, string otherDir)
    {
        InitializeComponent();
        Owner = owner;
        _settings = settings;
        _currentDir = currentDir;
        _otherDir = otherDir;
        SrcBox.Text = currentDir;
        DstBox.Text = otherDir;
        RefreshProcCombo(null);
    }

    private void RefreshProcCombo(string? select)
    {
        _loading = true;
        var names = new List<string> { "（手順を選択）" };
        names.AddRange(_settings.MirrorProcs.Keys.OrderBy(k => k));
        ProcCombo.ItemsSource = names;
        ProcCombo.SelectedItem = select ?? names[0];
        _loading = false;
    }

    private void Proc_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProcCombo.SelectedItem is not string name || !_settings.MirrorProcs.TryGetValue(name, out var p)) return;
        SrcBox.Text = p.Src; DstBox.Text = p.Dst;
        IncludeBox.Text = p.Include; ExcludeBox.Text = p.Exclude; ExcludeDirBox.Text = p.ExcludeDir;
        ConfirmCheck.IsChecked = p.Confirm; DeleteCheck.IsChecked = p.Delete; HiddenCheck.IsChecked = p.Hidden;
    }

    private MirrorProc Collect() => new()
    {
        Src = SrcBox.Text.Trim().Trim('"'), Dst = DstBox.Text.Trim().Trim('"'),
        Include = IncludeBox.Text.Trim(), Exclude = ExcludeBox.Text.Trim(), ExcludeDir = ExcludeDirBox.Text.Trim(),
        Confirm = ConfirmCheck.IsChecked == true, Delete = DeleteCheck.IsChecked == true, Hidden = HiddenCheck.IsChecked == true,
    };

    private void SaveProc_Click(object sender, RoutedEventArgs e)
    {
        var cur = ProcCombo.SelectedItem as string;
        var name = InputDialog.Show(this, "この手順を保存する名前", cur is not null && _settings.MirrorProcs.ContainsKey(cur) ? cur : Path.GetFileName(SrcBox.Text.TrimEnd('\\')));
        if (string.IsNullOrWhiteSpace(name)) return;
        _settings.MirrorProcs[name.Trim()] = Collect();
        _settings.Save();
        RefreshProcCombo(name.Trim());
    }

    private void DeleteProc_Click(object sender, RoutedEventArgs e)
    {
        if (ProcCombo.SelectedItem is not string name || !_settings.MirrorProcs.ContainsKey(name)) return;
        if (MessageBox.Show(this, $"手順「{name}」を削除しますか？", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _settings.MirrorProcs.Remove(name);
        _settings.Save();
        RefreshProcCombo(null);
    }

    private void SrcCurrent_Click(object sender, RoutedEventArgs e) => SrcBox.Text = _currentDir;
    private void DstOther_Click(object sender, RoutedEventArgs e) => DstBox.Text = _otherDir;
    private void DstJump_Click(object sender, RoutedEventArgs e)
    {
        var w = new JumpWindow(this, _settings, DstBox.Text);
        w.ShowDialog();
        if (w.SelectedPath is { } p) DstBox.Text = p;
    }
    private void SrcBrowse_Click(object sender, RoutedEventArgs e) => Browse(SrcBox, "複写元フォルダ");
    private void DstBrowse_Click(object sender, RoutedEventArgs e) => Browse(DstBox, "複写先フォルダ");
    private void Browse(TextBox box, string title)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = title, UseDescriptionForTitle = true, InitialDirectory = Directory.Exists(box.Text) ? box.Text : _currentDir };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) box.Text = dlg.SelectedPath;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var p = Collect();
        if (!Directory.Exists(p.Src)) { MessageBox.Show(this, "複写元フォルダがありません。", Title); return; }
        if (p.Dst.Length == 0) { MessageBox.Show(this, "複写先フォルダを指定してください。", Title); return; }
        var s = Path.GetFullPath(p.Src + "\\"); var d = Path.GetFullPath(p.Dst + "\\");
        if (string.Equals(s, d, StringComparison.OrdinalIgnoreCase) || d.StartsWith(s, StringComparison.OrdinalIgnoreCase) || s.StartsWith(d, StringComparison.OrdinalIgnoreCase))
        { MessageBox.Show(this, "複写元と複写先が同じか、入れ子になっています。", Title); return; }
        Proc = p;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (AccessKeys.TryHandle(this, e)) { e.Handled = true; return; }   // (_X) の X キー（Alt なし）
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }

    /// <summary>robocopy の引数を組み立てる。dryRun=true なら /L（一覧のみ）。</summary>
    public static string BuildArgs(MirrorProc p, bool dryRun)
    {
        string Q(string s) => "\"" + s.TrimEnd('\\') + "\"";
        var parts = new List<string> { Q(p.Src), Q(p.Dst) };
        parts.AddRange(p.Include.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries).Select(Q));
        parts.Add(p.Delete ? "/MIR" : "/E");
        if (p.Exclude.Length > 0) { parts.Add("/XF"); parts.AddRange(p.Exclude.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries).Select(Q)); }
        if (p.ExcludeDir.Length > 0) { parts.Add("/XD"); parts.AddRange(p.ExcludeDir.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries).Select(Q)); }
        if (!p.Hidden) parts.Add("/XA:HS");
        parts.AddRange(new[] { "/R:1", "/W:1", "/NP", "/NJH", "/UNICODE" });
        if (dryRun) parts.Add("/L");
        return string.Join(" ", parts);
    }
}
