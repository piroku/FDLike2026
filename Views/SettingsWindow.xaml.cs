using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FDLike2026.Services;

namespace FDLike2026.Views;

/// <summary>「指定する部分」の 1 行。</summary>
public sealed class PartRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Key { get; }
    public string Label { get; }
    private string _hex;
    public string Hex
    {
        get => _hex;
        set
        {
            _hex = ThemeService.ToHex(ThemeService.ParseColor(value));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Hex)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Brush)));
        }
    }
    public Brush Brush => new SolidColorBrush(ThemeService.ParseColor(_hex));
    public PartRow(string key, string label, string hex) { Key = key; Label = label; _hex = hex; }
}

/// <summary>
/// WinFM2008 の「カスタマイズ」相当。配色（名前付き / 部位ごと / RGB）・領域別フォント・行間・タグマーク・操作設定。
/// 変更は即時プレビューし、キャンセルで元に戻す。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly Theme _original;
    private readonly string _originalScheme;
    private Theme _work;
    private readonly List<PartRow> _rows = new();
    private bool _loading = true;

    public SettingsWindow(Window owner, Settings settings)
    {
        InitializeComponent();
        Owner = owner;
        _settings = settings;
        _original = settings.Theme.Clone();
        _originalScheme = settings.CurrentScheme;
        _work = settings.Theme.Clone();

        RefreshSchemeCombo(settings.CurrentScheme);
        BuildRows();
        PartList.SelectedIndex = 0;

        TagArrow.IsChecked = _work.TagMark != "star";
        TagStar.IsChecked = _work.TagMark == "star";
        GapBox.Text = _work.LineGap.ToString();
        TreeGapBox.Text = _work.TreeLineGap.ToString();
        ViewerGapBox.Text = _work.ViewerLineGap.ToString();
        RenderClearType.IsChecked = _work.TextRendering == "cleartype";
        RenderGray.IsChecked = _work.TextRendering == "grayscale";
        RenderAliased.IsChecked = _work.TextRendering == "aliased";
        UpdateFontTexts();

        EditorBox.Text = settings.EditorPath;
        EditorLineBox.Text = settings.EditorLineArgs;
        DiffToolBox.Text = settings.DiffTool;
        RecycleCheck.IsChecked = settings.DeleteToRecycleBin;
        HiddenCheck.IsChecked = settings.ShowHidden;
        IconCheck.IsChecked = settings.ShowIcons;
        GroupFirst.IsChecked = settings.HiddenGrouping == "first";
        GroupLast.IsChecked = settings.HiddenGrouping == "last";
        GroupMixed.IsChecked = settings.HiddenGrouping == "mixed";

        // リスト
        _modes = settings.ListModes.Select(m => m.Clone()).ToList();
        RefreshModeList();
        StartupModeCombo.ItemsSource = Enumerable.Range(1, 5).Select(i => $"モード {i}").ToList();
        StartupModeCombo.SelectedIndex = Math.Clamp(settings.StartupListMode, 0, 4);
        DirLabelCombo.Text = settings.DirLabel;
        DirLabelCombo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => PreviewListMode()));
        DirLabelCombo.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(PreviewListMode);
        _originalModes = settings.ListModes.Select(m => m.Clone()).ToList();
        _originalDirLabel = settings.DirLabel;
        _modeIndex = Math.Clamp(ListModeService.CurrentIndex, 0, 4);
        ModeList.SelectedIndex = _modeIndex;   // いま表示中のモードを選んでおく

        _loading = false;
        LoadPartIntoEditor();
        LoadModeIntoEditor();
    }

    // =====================================================================
    // リスト（表示モード）
    // =====================================================================
    private List<ListMode> _modes = new();
    private List<ListMode> _originalModes = new();
    private string _originalDirLabel = "< DIR >";
    private int _modeIndex;

    /// <summary>編集中のモードが表示中のモードなら、メイン画面へ即時反映する。</summary>
    private void PreviewListMode()
    {
        if (_loading || _modes.Count == 0) return;
        int cur = Math.Clamp(ListModeService.CurrentIndex, 0, 4);
        var label = string.IsNullOrWhiteSpace(DirLabelCombo.Text) ? "< DIR >" : DirLabelCombo.Text.Trim();
        ListModeService.Apply(_modes[cur], label, _work);
        ListModeService.RaiseChanged();
    }

    private void RefreshModeList()
    {
        int sel = ModeList.SelectedIndex;
        ModeList.ItemsSource = _modes.Select((m, i) => $"Shift+{i + 1}   {m.Name}").ToList();
        if (sel >= 0) { _loading = true; ModeList.SelectedIndex = sel; _loading = false; }
    }

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ModeList.SelectedIndex < 0) return;
        _modeIndex = ModeList.SelectedIndex;
        LoadModeIntoEditor();
    }

    private void LoadModeIntoEditor()
    {
        var m = _modes[_modeIndex];
        _loading = true;
        ModeNameBox.Text = m.Name;
        RightPaneCheck.IsChecked = m.ShowRightPane;
        GitCheck.IsChecked = m.ShowGit;
        OverflowCheck.IsChecked = m.ShowOverflow;
        NameFixed.IsChecked = m.FileNameMode == "fixed";
        NameDos.IsChecked = m.FileNameMode == "dos";
        NameAuto.IsChecked = m.FileNameMode is not ("fixed" or "dos");
        NameCharsBox.Text = m.FileNameChars.ToString();
        SplitExtCheck.IsChecked = m.SplitExt;
        ExtCharsBox.Text = m.ExtChars.ToString();
        DirNoSplitCheck.IsChecked = m.DirNoSplit;
        DateKindCombo.SelectedIndex = m.DateKind == "create" ? 1 : m.DateKind == "access" ? 2 : 0;
        ShowDateCheck.IsChecked = m.ShowDate;
        Year4.IsChecked = m.Year4;
        Year2.IsChecked = !m.Year4;
        ShowTimeCheck.IsChecked = m.ShowTime;
        SecondsCheck.IsChecked = m.ShowSeconds;
        DatePadBox.Text = m.DatePad.ToString();
        ShowSizeCheck.IsChecked = m.ShowSize;
        SizeShortCheck.IsChecked = m.SizeShort;
        ShowAttrCheck.IsChecked = m.ShowAttr;
        AttrFilmtnCheck.IsChecked = m.AttrFilmtn;
        _loading = false;
    }

    private static int Int(TextBox box, int fallback, int min, int max)
        => int.TryParse(box.Text.Trim(), out var v) ? Math.Clamp(v, min, max) : fallback;

    /// <summary>右側の項目が変わったら、選択中モードへ書き戻す。</summary>
    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _modes.Count == 0) return;
        var m = _modes[_modeIndex];
        m.ShowRightPane = RightPaneCheck.IsChecked == true;
        m.ShowGit = GitCheck.IsChecked == true;
        m.ShowOverflow = OverflowCheck.IsChecked == true;
        m.FileNameMode = NameFixed.IsChecked == true ? "fixed" : NameDos.IsChecked == true ? "dos" : "auto";
        m.FileNameChars = Int(NameCharsBox, m.FileNameChars, 4, 200);
        m.SplitExt = SplitExtCheck.IsChecked == true;
        m.ExtChars = Int(ExtCharsBox, m.ExtChars, 1, 30);
        m.DirNoSplit = DirNoSplitCheck.IsChecked == true;
        m.DateKind = DateKindCombo.SelectedIndex == 1 ? "create" : DateKindCombo.SelectedIndex == 2 ? "access" : "write";
        m.ShowDate = ShowDateCheck.IsChecked == true;
        m.Year4 = Year4.IsChecked == true;
        m.ShowTime = ShowTimeCheck.IsChecked == true;
        m.ShowSeconds = SecondsCheck.IsChecked == true;
        m.DatePad = Int(DatePadBox, m.DatePad, 0, 10);
        m.ShowSize = ShowSizeCheck.IsChecked == true;
        m.SizeShort = SizeShortCheck.IsChecked == true;
        m.ShowAttr = ShowAttrCheck.IsChecked == true;
        m.AttrFilmtn = AttrFilmtnCheck.IsChecked == true;
        PreviewListMode();
    }

    private void ModeName_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || _modes.Count == 0) return;
        _modes[_modeIndex].Name = ModeNameBox.Text.Trim().Length == 0 ? $"モード:{_modeIndex + 1}" : ModeNameBox.Text.Trim();
        RefreshModeList();
    }

    // =====================================================================
    // 配色（名前付き）
    // =====================================================================
    private void RefreshSchemeCombo(string select)
    {
        _loading = true;
        var names = Theme.BuiltIn.Concat(_settings.Schemes.Keys.OrderBy(k => k)).Distinct().ToList();
        if (!names.Contains(select)) names.Insert(0, select);
        SchemeCombo.ItemsSource = names;
        SchemeCombo.SelectedItem = select;
        _loading = false;
    }

    private void Scheme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SchemeCombo.SelectedItem is not string name) return;
        Theme t;
        if (_settings.Schemes.TryGetValue(name, out var user)) t = user.Clone();
        else if (Theme.BuiltIn.Contains(name)) t = Theme.GetBuiltIn(name);
        else return;
        // フォント・行間・タグマークは配色に含めず、現在値を維持（WinFM と同じく「配色」は色だけ）
        t.InfoFont = _work.InfoFont; t.ListFont = _work.ListFont; t.TreeFont = _work.TreeFont; t.OtherFont = _work.OtherFont;
        t.LineGap = _work.LineGap; t.TreeLineGap = _work.TreeLineGap; t.ViewerLineGap = _work.ViewerLineGap; t.TagMark = _work.TagMark; t.TextRendering = _work.TextRendering;
        t.FillMissing();
        _work = t;
        _settings.CurrentScheme = name;
        BuildRows();
        PartList.SelectedIndex = Math.Max(0, PartList.SelectedIndex);
        LoadPartIntoEditor();
        Preview();
    }

    private void SchemeSave_Click(object sender, RoutedEventArgs e)
    {
        var cur = SchemeCombo.SelectedItem as string ?? "";
        var name = InputDialog.Show(this, "この配色を保存する名前", Theme.BuiltIn.Contains(cur) ? cur + " (カスタム)" : cur);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (Theme.BuiltIn.Contains(name))
        {
            MessageBox.Show(this, "組み込みの配色名は上書きできません。別の名前を付けてください。", "カスタマイズ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var saved = _work.Clone();
        _settings.Schemes[name] = saved;
        _settings.CurrentScheme = name;
        _settings.Save();
        RefreshSchemeCombo(name);
    }

    private void SchemeDelete_Click(object sender, RoutedEventArgs e)
    {
        if (SchemeCombo.SelectedItem is not string name) return;
        if (Theme.BuiltIn.Contains(name))
        {
            MessageBox.Show(this, "組み込みの配色は削除できません。", "カスタマイズ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"配色「{name}」を削除しますか？", "カスタマイズ", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _settings.Schemes.Remove(name);
        _settings.Save();
        RefreshSchemeCombo("Classic");
        Scheme_Changed(sender, null!);
    }

    /// <summary>色を 1 つでも変えたら、コンボの表示を「(変更あり)」付きの一時名にする。</summary>
    private void MarkModified()
    {
        if (SchemeCombo.SelectedItem is string name && !name.EndsWith("(変更あり)"))
        {
            var temp = name + " (変更あり)";
            _settings.CurrentScheme = temp;
            RefreshSchemeCombo(temp);
        }
    }

    // =====================================================================
    // 指定する部分 / RGB
    // =====================================================================
    private void BuildRows()
    {
        var keep = (PartList.SelectedItem as PartRow)?.Key;
        _rows.Clear();
        foreach (var it in Theme.Items)
            _rows.Add(new PartRow(it.Key, it.Label, _work.Colors[it.Key]));
        PartList.ItemsSource = null;
        PartList.ItemsSource = _rows;
        if (keep is not null) PartList.SelectedItem = _rows.FirstOrDefault(r => r.Key == keep);
    }

    private PartRow? CurrentPart => PartList.SelectedItem as PartRow;

    private void Part_Changed(object sender, SelectionChangedEventArgs e) => LoadPartIntoEditor();

    private void LoadPartIntoEditor()
    {
        if (CurrentPart is not { } row) return;
        var c = ThemeService.ParseColor(row.Hex);
        _loading = true;
        RSlider.Value = c.R; GSlider.Value = c.G; BSlider.Value = c.B;
        RBox.Text = c.R.ToString(); GBox.Text = c.G.ToString(); BBox.Text = c.B.ToString();
        HexBox.Text = row.Hex;
        Sample.Background = new SolidColorBrush(c);
        _loading = false;
    }

    private void SetCurrentColor(Color c)
    {
        if (CurrentPart is not { } row) return;
        row.Hex = ThemeService.ToHex(c);
        _work.Colors[row.Key] = row.Hex;
        LoadPartIntoEditor();
        MarkModified();
        Preview();
    }

    private void Rgb_SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        SetCurrentColor(Color.FromRgb((byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value));
    }

    private void Rgb_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (byte.TryParse(RBox.Text, out var r) && byte.TryParse(GBox.Text, out var g) && byte.TryParse(BBox.Text, out var b))
            SetCurrentColor(Color.FromRgb(r, g, b));
        else LoadPartIntoEditor();
    }

    private void Rgb_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Rgb_TextChanged(sender, e); e.Handled = true; }
    }

    private void Hex_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var t = HexBox.Text.Trim();
        if (!t.StartsWith('#')) t = "#" + t;
        if (t.Length == 7) SetCurrentColor(ThemeService.ParseColor(t));
        else LoadPartIntoEditor();
    }

    private void Hex_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Hex_Changed(sender, e); e.Handled = true; }
    }

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPart is not { } row) return;
        var cur = ThemeService.ParseColor(row.Hex);
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B),
            CustomColors = CgaPalette(),
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SetCurrentColor(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
    }

    private void ResetPart_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentPart is not { } row) return;
        var baseName = (SchemeCombo.SelectedItem as string ?? "Classic").Replace(" (変更あり)", "");
        var def = _settings.Schemes.TryGetValue(baseName, out var u) ? u : Theme.GetBuiltIn(baseName);
        SetCurrentColor(ThemeService.ParseColor(def.Colors.TryGetValue(row.Key, out var h) ? h : "#FFFFFF"));
    }

    /// <summary>DOS 時代の 16 色をカスタムカラー欄に並べておく（Win32 の ColorDialog は BGR 順）。</summary>
    private static int[] CgaPalette()
    {
        string[] hex = { "000000", "0000AA", "00AA00", "00AAAA", "AA0000", "AA00AA", "AA5500", "AAAAAA",
                         "555555", "5555FF", "55FF55", "55FFFF", "FF5555", "FF55FF", "FFFF55", "FFFFFF" };
        return hex.Select(h =>
        {
            int r = Convert.ToInt32(h[..2], 16), g = Convert.ToInt32(h[2..4], 16), b = Convert.ToInt32(h[4..], 16);
            return r | (g << 8) | (b << 16);
        }).ToArray();
    }

    // =====================================================================
    // フォント / 行間 / タグマーク
    // =====================================================================
    private FontSpec FontOf(string area) => area switch
    {
        "Info" => _work.InfoFont,
        "List" => _work.ListFont,
        "Tree" => _work.TreeFont,
        _ => _work.OtherFont,
    };

    private void UpdateFontTexts()
    {
        static string D(FontSpec f) => $"{f.Family}  {f.Size * 72 / 96:0.#} pt{(f.Bold ? "  太字" : "")}";
        InfoFontText.Text = D(_work.InfoFont);
        ListFontText.Text = D(_work.ListFont);
        TreeFontText.Text = D(_work.TreeFont);
        OtherFontText.Text = D(_work.OtherFont);
    }

    private void Font_Click(object sender, RoutedEventArgs e)
    {
        var area = (sender as Button)?.Tag as string ?? "Other";
        var f = FontOf(area);
        using var dlg = new System.Windows.Forms.FontDialog
        {
            ShowEffects = false,
            ShowColor = false,
            FontMustExist = true,
            MinSize = 6,
            MaxSize = 36,
        };
        try
        {
            dlg.Font = new System.Drawing.Font(f.Family, (float)(f.Size * 72 / 96),
                f.Bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular);
        }
        catch { }
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        f.Family = dlg.Font.Name;
        f.Size = Math.Round(dlg.Font.SizeInPoints * 96 / 72, 1);
        f.Bold = dlg.Font.Bold;
        UpdateFontTexts();
        Preview();
    }

    private void Gap_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (int.TryParse(GapBox.Text, out var g)) SetGap(g);
        else GapBox.Text = _work.LineGap.ToString();
    }

    private void Gap_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Gap_Changed(sender, e); e.Handled = true; }
        else if (e.Key == Key.Up) { SetGap(_work.LineGap + 1); e.Handled = true; }
        else if (e.Key == Key.Down) { SetGap(_work.LineGap - 1); e.Handled = true; }
    }

    private void GapUp_Click(object sender, RoutedEventArgs e) => SetGap(_work.LineGap + 1);
    private void GapDown_Click(object sender, RoutedEventArgs e) => SetGap(_work.LineGap - 1);

    private void SetGap(int g)
    {
        _work.LineGap = Math.Clamp(g, 0, 20);
        GapBox.Text = _work.LineGap.ToString();
        Preview();
    }

    private void TreeGap_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (int.TryParse(TreeGapBox.Text, out var g)) SetTreeGap(g);
        else TreeGapBox.Text = _work.TreeLineGap.ToString();
    }

    private void TreeGap_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { TreeGap_Changed(sender, e); e.Handled = true; }
        else if (e.Key == Key.Up) { SetTreeGap(_work.TreeLineGap + 1); e.Handled = true; }
        else if (e.Key == Key.Down) { SetTreeGap(_work.TreeLineGap - 1); e.Handled = true; }
    }

    private void TreeGapUp_Click(object sender, RoutedEventArgs e) => SetTreeGap(_work.TreeLineGap + 1);
    private void TreeGapDown_Click(object sender, RoutedEventArgs e) => SetTreeGap(_work.TreeLineGap - 1);

    private void SetTreeGap(int g)
    {
        _work.TreeLineGap = Math.Clamp(g, 0, 20);
        TreeGapBox.Text = _work.TreeLineGap.ToString();
        Preview();
    }

    private void ViewerGap_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (int.TryParse(ViewerGapBox.Text, out var g)) SetViewerGap(g);
        else ViewerGapBox.Text = _work.ViewerLineGap.ToString();
    }

    private void ViewerGap_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ViewerGap_Changed(sender, e); e.Handled = true; }
        else if (e.Key == Key.Up) { SetViewerGap(_work.ViewerLineGap + 1); e.Handled = true; }
        else if (e.Key == Key.Down) { SetViewerGap(_work.ViewerLineGap - 1); e.Handled = true; }
    }

    private void ViewerGapUp_Click(object sender, RoutedEventArgs e) => SetViewerGap(_work.ViewerLineGap + 1);
    private void ViewerGapDown_Click(object sender, RoutedEventArgs e) => SetViewerGap(_work.ViewerLineGap - 1);

    private void SetViewerGap(int g)
    {
        _work.ViewerLineGap = Math.Clamp(g, 0, 20);
        ViewerGapBox.Text = _work.ViewerLineGap.ToString();
        Preview();
    }

    private void Render_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _work.TextRendering = RenderAliased.IsChecked == true ? "aliased" : RenderGray.IsChecked == true ? "grayscale" : "cleartype";
        Preview();
    }

    private void TagMark_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _work.TagMark = TagStar.IsChecked == true ? "star" : "arrow";
        Preview();
    }

    // =====================================================================
    // 共通
    // =====================================================================
    private void Preview() { ThemeService.Apply(_work); ListModeService.RaiseChanged(); }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _settings.Theme = _work;
        _settings.EditorPath = string.IsNullOrWhiteSpace(EditorBox.Text) ? "notepad.exe" : EditorBox.Text.Trim();
        _settings.EditorLineArgs = EditorLineBox.Text.Trim();
        _settings.DiffTool = DiffToolBox.Text.Trim();
        _settings.DeleteToRecycleBin = RecycleCheck.IsChecked == true;
        _settings.ShowHidden = HiddenCheck.IsChecked == true;
        _settings.ShowIcons = IconCheck.IsChecked == true;
        ThemeService.ApplyIcons(_settings.ShowIcons);
        _settings.HiddenGrouping = GroupLast.IsChecked == true ? "last" : GroupMixed.IsChecked == true ? "mixed" : "first";
        Field_Changed(sender, e);
        _settings.ListModes = _modes;
        _settings.StartupListMode = Math.Max(0, StartupModeCombo.SelectedIndex);
        _settings.DirLabel = string.IsNullOrWhiteSpace(DirLabelCombo.Text) ? "< DIR >" : DirLabelCombo.Text.Trim();
        _settings.Save();
        ThemeService.Apply(_work);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        RestoreAll();
        DialogResult = false;
    }

    private void RestoreAll()
    {
        _settings.CurrentScheme = _originalScheme;
        ThemeService.Apply(_original);
        ListModeService.Apply(_originalModes[Math.Clamp(ListModeService.CurrentIndex, 0, 4)], _originalDirLabel, _original);
        ListModeService.RaiseChanged();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Cancel_Click(sender, e); e.Handled = true; }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DialogResult is null) RestoreAll();
        base.OnClosing(e);
    }
}
