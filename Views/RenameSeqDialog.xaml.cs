using System.IO;
using System.Windows;
using System.Windows.Input;
using FDLike2026.Models;

namespace FDLike2026.Views;

public sealed record RenamePlan(FileEntry Entry, string OldName, string NewName);

/// <summary>ファイル名の一括変換（連番）。WinFM2008 の「名前の一括変換」相当。</summary>
public partial class RenameSeqDialog : Window
{
    private readonly IReadOnlyList<FileEntry> _items;
    private bool _ready;

    public List<RenamePlan> Plans { get; private set; } = new();
    public bool ConsiderExisting => ConsiderCheck.IsChecked == true;

    public RenameSeqDialog(Window owner, IReadOnlyList<FileEntry> items)
    {
        InitializeComponent();
        Owner = owner;
        _items = items;
        // 固定文字列の既定: 最初のファイルの名前から末尾の数字を除いたもの
        var first = items.Count > 0 ? (items[0].IsDirectory ? items[0].Name : Path.GetFileNameWithoutExtension(items[0].Name)) : "file";
        NameFixedBox.Text = first.TrimEnd("0123456789_- ".ToCharArray());
        if (NameFixedBox.Text.Length == 0) NameFixedBox.Text = "file";
        _ready = true;
        Rebuild();
    }

    private void Changed(object sender, RoutedEventArgs e) => Rebuild();

    private static int Int(System.Windows.Controls.TextBox tb, int def) => int.TryParse(tb.Text, out var v) ? v : def;

    private void Rebuild()
    {
        if (!_ready) return;
        bool doName = NameCheck.IsChecked == true, doExt = ExtCheck.IsChecked == true;
        bool reuse = ReuseCheck.IsChecked == true;
        int digits = Math.Clamp(Int(NameDigitsBox, 3), 0, 10), start = Int(NameStartBox, 1), step = Math.Max(1, Int(NameStepBox, 1));
        int extDigits = Math.Clamp(Int(ExtDigitsBox, 0), 0, 10), extStart = Int(ExtStartBox, 1);
        string fixedName = NameFixedBox.Text, fixedExt = ExtFixedBox.Text.TrimStart('.'), sep = SepBox.Text;
        bool head = PosHead.IsChecked == true, both = PosBoth.IsChecked == true;

        var plans = new List<RenamePlan>();
        int n = start, en = extStart;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in _items)
        {
            string stem = it.IsDirectory ? it.Name : Path.GetFileNameWithoutExtension(it.Name);
            string ext = it.IsDirectory ? "" : Path.GetExtension(it.Name).TrimStart('.');

            if (doName)
            {
                string num = digits > 0 ? n.ToString(new string('0', digits)) : n.ToString();
                string baseName = reuse ? stem : fixedName;
                stem = both ? num + sep + baseName + sep + num
                     : head ? num + sep + baseName
                     : baseName + sep + num;
                if (baseName.Length == 0) stem = num;
                n += step;
            }
            if (doExt && !it.IsDirectory)
            {
                string enum_ = extDigits > 0 ? en.ToString(new string('0', extDigits)) : "";
                ext = fixedExt + enum_;
                en++;
            }
            var newName = ext.Length > 0 ? stem + "." + ext : stem;
            foreach (var c in Path.GetInvalidFileNameChars()) newName = newName.Replace(c, '_');
            // 同じ結果名が出たら枝番を足す
            var candidate = newName; int k = 2;
            while (!used.Add(candidate)) candidate = Path.GetFileNameWithoutExtension(newName) + $"({k++})" + Path.GetExtension(newName);
            plans.Add(new RenamePlan(it, it.Name, candidate));
        }
        Plans = plans;
        Preview.ItemsSource = plans;
        int changed = plans.Count(p => !string.Equals(p.OldName, p.NewName, StringComparison.Ordinal));
        SummaryText.Text = $"{plans.Count} 件中 {changed} 件が変わります";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Plans.All(p => p.OldName == p.NewName)) { MessageBox.Show(this, "変更される名前がありません。", Title); return; }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }
}
