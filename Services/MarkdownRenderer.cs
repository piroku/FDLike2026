using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using MdTable = Markdig.Extensions.Tables.Table;
using MdRow = Markdig.Extensions.Tables.TableRow;
using MdCell = Markdig.Extensions.Tables.TableCell;
using TableColumnAlign = Markdig.Extensions.Tables.TableColumnAlign;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FDLike2026.Services;

/// <summary>
/// Markdown → FlowDocument（ビューア用）。Markdig で解析し、FDLike の配色（紺地）に合わせて描く。
/// 見出し / 段落 / 箇条書き・番号付き / コード（ブロック・インライン） / 引用 / 罫線 / 表 / 強調 / リンク に対応。
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static Brush Res(string key, Brush fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? fallback;

    // 色は F10 › デザイン の「Markdown」グループから（無ければ従来のテーマ色）
    private static Brush Fg => Res("MdFgBrush", Res("OtherFgBrush", Brushes.White));
    private static Brush Head => Res("MdHeadingBrush", Res("InfoTitleBrush", Brushes.Yellow));
    private static Brush Accent => Res("MdLinkBrush", Res("InfoItemBrush", Brushes.Cyan));
    private static Brush Dim => Res("MdDimBrush", Res("OtherDimBrush", Brushes.Gray));
    private static Brush CodeFg => Res("MdCodeBrush", Res("InfoTitleBrush", Brushes.Yellow));
    private static Brush CodeBg => Res("MdCodeBgBrush", CodeHighlighter.Background);
    private static Brush Frame => Res("MdRuleBrush", Res("FrameBrush", Brushes.Cyan));
    private static Brush TableHead => Res("MdTableHeadBrush", Head);
    private static Brush QuoteFg => Res("MdQuoteBrush", Fg);
    private static Brush QuoteBg => Res("MdQuoteBgBrush", new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)));
    public static Brush PageBg => Res("MdBgBrush", Brushes.Transparent);

    private static double _gap;   // 行間ギャップ（lineHeight − フォントの自然な行高）
    private static FontFamily _font = new("BIZ UDゴシック");

    /// <summary>true: 表をウィンドウ幅に収めてセル内で折り返す / false: 内容幅のまま横スクロール</summary>
    public static bool WrapTables { get; set; } = false;
    /// <summary>表を収める幅（ビューアの表示幅）。0 なら制限なし。</summary>
    public static double AvailableWidth { get; set; } = 0;

    public static FlowDocument Render(string markdown, FontFamily font, double fontSize, double lineHeight)
    {
        _font = font;
        _gap = Math.Max(0, lineHeight - Math.Ceiling(fontSize * font.LineSpacing));
        var doc = new FlowDocument
        {
            FontFamily = font,
            FontSize = fontSize,
            Foreground = Fg,
            Background = PageBg,
            PagePadding = new Thickness(12, 8, 12, 8),
            TextAlignment = TextAlignment.Left,   // 既定の両端揃えだと折り返し位置で空白が伸びる
            LineHeight = lineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            ColumnWidth = double.PositiveInfinity,   // 段組みしない
        };
        _maxContentWidth = 0;
        var md = Markdown.Parse(markdown, Pipeline);
        foreach (var block in md) foreach (var b in RenderBlock(block, doc)) doc.Blocks.Add(b);
        // 表やコードがウィンドウより広いときは、折り返さず横スクロールで見られるように最小ページ幅を広げる
        // 折り返しモードでは幅を広げない（* 列の表がページ幅いっぱいに伸びて崩れるため）
        // ページ幅は常にビューポート幅（本文は折り返し）。幅の広い表は個別の横スクロール枠に入れている
        return doc;
    }

    /// <summary>表（列幅の合計）やコードブロック（最長行）の必要幅。Render ごとにリセット。</summary>
    private static double _maxContentWidth;

    /// <summary>
    /// ソースファイル全体を VS Code 風（黒地・行番号・シンタックスハイライト）の FlowDocument にする。
    /// </summary>
    public static FlowDocument RenderCode(string code, string lang, FontFamily font, double fontSize, double lineHeight)
        => RenderCode(code, lang, font, fontSize, lineHeight, null, 0, out _);

    /// <summary>
    /// ソース表示（行番号付き）。search に一致する行には行番号の左に ► マークと一致部分の強調、
    /// targetLine の行は行全体を帯で示す。anchor にその行の先頭 Inline を返す（BringIntoView 用）。
    /// </summary>
    public static FlowDocument RenderCode(string code, string lang, FontFamily font, double fontSize, double lineHeight,
                                          System.Text.RegularExpressions.Regex? search, int targetLine, out System.Windows.Documents.Inline? anchor)
    {
        anchor = null;
        var doc = new FlowDocument
        {
            FontFamily = font, FontSize = fontSize,
            Foreground = CodeHighlighter.Plain, Background = CodeHighlighter.Background,
            PagePadding = new Thickness(8, 6, 8, 6),
            LineHeight = lineHeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            ColumnWidth = double.PositiveInfinity,
        };
        code = code.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = code.Split('\n');
        int digits = Math.Max(2, lines.Length.ToString().Length);
        var numBrush = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85)); numBrush.Freeze();
        var markBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0x00)); markBrush.Freeze();
        var hitBg = new SolidColorBrush(Color.FromArgb(0x90, 0xC0, 0x30, 0x00)); hitBg.Freeze();          // 一致部分
        var targetBg = new SolidColorBrush(Color.FromArgb(0x60, 0x26, 0x4F, 0x78)); targetBg.Freeze();    // 該当行の帯（VS Code の選択色風）

        // 一致行を先に調べる
        var hitLines = new HashSet<int>();
        if (search is not null)
            for (int i = 0; i < lines.Length; i++) { try { if (search.IsMatch(lines[i])) hitLines.Add(i + 1); } catch { } }

        var p = new Paragraph { Margin = new Thickness(0) };
        int line = 1;
        Span? lineSpan = null;   // 該当行の帯用
        System.Windows.Documents.Inline? anchorLocal = null;

        System.Windows.Documents.Inline Num(int n)
        {
            bool hit = hitLines.Contains(n);
            var r = new Run((hit ? "►" : " ") + n.ToString().PadLeft(digits) + "  ") { Foreground = hit ? markBrush : numBrush };
            if (hit) r.FontWeight = FontWeights.Bold;
            return r;
        }
        void BeginLine(int n)
        {
            var num = Num(n);
            if (n == targetLine)
            {
                lineSpan = new Span { Background = targetBg };
                p.Inlines.Add(lineSpan);
                lineSpan.Inlines.Add(num);
                anchorLocal = num;
            }
            else { lineSpan = null; p.Inlines.Add(num); }
        }
        void AddText(string text, Brush fg)
        {
            if (text.Length == 0) return;
            var target = lineSpan?.Inlines ?? p.Inlines;
            if (search is not null && hitLines.Contains(line))
            {
                // 一致部分だけ背景を付ける
                int pos = 0;
                foreach (System.Text.RegularExpressions.Match m in search.Matches(text))
                {
                    if (m.Length == 0) continue;
                    if (m.Index > pos) target.Add(new Run(text[pos..m.Index]) { Foreground = fg });
                    target.Add(new Run(m.Value) { Foreground = Brushes.White, Background = hitBg, FontWeight = FontWeights.Bold });
                    pos = m.Index + m.Length;
                }
                if (pos < text.Length) target.Add(new Run(text[pos..]) { Foreground = fg });
            }
            else target.Add(new Run(text) { Foreground = fg });
        }

        BeginLine(line);
        foreach (var run in CodeHighlighter.Highlight(code, lang))
        {
            var parts = run.Text.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) { p.Inlines.Add(new LineBreak()); line++; BeginLine(line); }
                AddText(parts[i], run.Foreground);
            }
        }
        doc.Blocks.Add(p);
        anchor = anchorLocal;
        return doc;
    }

    private static IEnumerable<System.Windows.Documents.Block> RenderBlock(Markdig.Syntax.Block block, FlowDocument doc)
    {
        switch (block)
        {
            case HeadingBlock h:
            {
                double scale = h.Level switch { 1 => 1.6, 2 => 1.35, 3 => 1.15, _ => 1.0 };
                double size = doc.FontSize * scale;
                // 見出しは自分のフォントサイズに合わせた行高 + 同じ行間ギャップ（本文の行高のままだと上下が詰まる）
                var p = new Paragraph
                {
                    FontSize = size, FontWeight = FontWeights.Bold, Foreground = Head,
                    Margin = new Thickness(0, h.Level <= 2 ? 18 : 12, 0, h.Level <= 2 ? 10 : 6),
                    LineHeight = Math.Ceiling(size * _font.LineSpacing) + _gap,
                };
                AddInlines(p.Inlines, h.Inline);
                if (h.Level <= 2)
                {
                    // VS Code 風: 文字と下線の間に余白をとる
                    p.BorderBrush = Frame;
                    p.BorderThickness = new Thickness(0, 0, 0, 1);
                    p.Padding = new Thickness(0, 0, 0, 6);
                }
                yield return p;
                break;
            }
            case ParagraphBlock para:
            {
                var p = new Paragraph { Margin = new Thickness(0, 2, 0, 6) };
                AddInlines(p.Inlines, para.Inline);
                yield return p;
                break;
            }
            case ListBlock list:
            {
                var l = new List
                {
                    MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Margin = new Thickness(0, 2, 0, 6),
                    MarkerOffset = 8,
                    Padding = new Thickness(28, 0, 0, 0),
                };
                if (list.IsOrdered && int.TryParse(list.OrderedStart, out var start)) l.StartIndex = start;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var li = new ListItem { Margin = new Thickness(0, 1, 0, 1) };
                    foreach (var child in item) foreach (var b in RenderBlock(child, doc)) li.Blocks.Add(b);
                    l.ListItems.Add(li);
                }
                yield return l;
                break;
            }
            case FencedCodeBlock or CodeBlock:
            {
                var code = (CodeBlock)block;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < code.Lines.Count; i++) sb.Append(code.Lines.Lines[i].Slice.ToString()).Append('\n');
                var info = (block as FencedCodeBlock)?.Info;
                // 最長行の幅（横スクロール用）
                try
                {
                    var longest = sb.ToString().Split('\n').OrderByDescending(l => l.Length).FirstOrDefault() ?? "";
                    // コードブロックはページ幅で折り返す（ページ幅は広げない）
                }
                catch { }
                // VS Code (Dark+) 風: 黒地にシンタックスハイライト
                var p = new Paragraph
                {
                    Background = CodeHighlighter.Background, Foreground = CodeHighlighter.Plain,
                    Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 4, 0, 8),
                    BorderBrush = Frame, BorderThickness = new Thickness(1),
                    FontFamily = doc.FontFamily,
                };
                foreach (var run in CodeHighlighter.Highlight(sb.ToString().TrimEnd('\n'), info)) p.Inlines.Add(run);
                yield return p;
                break;
            }
            case QuoteBlock q:
            {
                // 引用（補足など）: 灰色だと紺地で読みにくいので、本文と同じ白＋薄い背景＋シアンの左罫線
                var sec = new Section
                {
                    BorderBrush = Accent, BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(10, 4, 8, 4), Margin = new Thickness(0, 4, 0, 8),
                    Foreground = QuoteFg, Background = QuoteBg,
                };
                foreach (var child in q) foreach (var b in RenderBlock(child, doc)) sec.Blocks.Add(b);
                yield return sec;
                break;
            }
            case ThematicBreakBlock:
                yield return new Paragraph { BorderBrush = Frame, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 8, 0, 8), FontSize = 2 };
                break;
            case MdTable table:
            {
                var t = new System.Windows.Documents.Table { CellSpacing = 0, BorderBrush = Frame, BorderThickness = new Thickness(1), Margin = new Thickness(0, 4, 0, 8) };
                int cols = table.ColumnDefinitions.Count;
                // 列幅は内容に合わせる。WPF の Table は Auto でも余白を列に配ってしまうので、
                // 各列の最長テキストを実測してピクセル幅で固定する（VS Code の見え方に合わせる）
                var widths = new double[cols];
                foreach (var row in table.OfType<MdRow>())
                {
                    int ci = 0;
                    foreach (var cell in row.OfType<MdCell>())
                    {
                        if (ci >= cols) break;
                        bool bold = row.IsHeader;
                        foreach (var line in PlainText(cell).Split('\n'))
                            widths[ci] = Math.Max(widths[ci], MeasureText(line, doc, bold));
                        ci++;
                    }
                }
                // 各列は内容幅（上限あり）。折り返しモードで表示幅を超えるときは、比率を保ったまま縮めてセル内で折り返す
                var colW = new double[cols];
                double total = 0;
                for (int i = 0; i < cols; i++) { colW[i] = Math.Clamp(widths[i] + 16, 40, 1200); total += colW[i]; }
                double avail = AvailableWidth - doc.PagePadding.Left - doc.PagePadding.Right - 8;
                if (WrapTables && avail > 100 && total > avail)
                {
                    double scale = avail / total;
                    for (int i = 0; i < cols; i++) colW[i] = Math.Max(40, Math.Floor(colW[i] * scale));
                }
                for (int i = 0; i < cols; i++) t.Columns.Add(new TableColumn { Width = new GridLength(colW[i]) });
                bool scrollable = !WrapTables && avail > 100 && total > avail;   // 表だけ横スクロール（本文は折り返したまま）
                var group = new TableRowGroup();
                foreach (var row in table.OfType<MdRow>())
                {
                    var tr = new System.Windows.Documents.TableRow();
                    if (row.IsHeader) { tr.FontWeight = FontWeights.Bold; tr.Foreground = TableHead; }
                    int ci = 0;
                    foreach (var cell in row.OfType<MdCell>())
                    {
                        var tc = new System.Windows.Documents.TableCell { BorderBrush = Frame, BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(6, 2, 6, 2) };
                        var align = ci < cols ? table.ColumnDefinitions[ci].Alignment : null;
                        tc.TextAlignment = align switch { TableColumnAlign.Center => TextAlignment.Center, TableColumnAlign.Right => TextAlignment.Right, _ => TextAlignment.Left };
                        foreach (var child in cell) foreach (var b in RenderBlock(child, doc)) { if (b is Paragraph pp) pp.Margin = new Thickness(0); tc.Blocks.Add(b); }
                        tr.Cells.Add(tc);
                        ci++;
                    }
                    group.Rows.Add(tr);
                }
                t.RowGroups.Add(group);
                if (scrollable)
                {
                    // 幅の広い表は、その表だけ横スクロールできる枠（読み取り専用 RichTextBox）に入れる
                    var inner = new FlowDocument
                    {
                        FontFamily = doc.FontFamily, FontSize = doc.FontSize, Foreground = doc.Foreground, Background = Brushes.Transparent,
                        PagePadding = new Thickness(0, 0, 0, 8), LineHeight = doc.LineHeight, LineStackingStrategy = doc.LineStackingStrategy,
                        ColumnWidth = double.PositiveInfinity, PageWidth = total + 24,
                    };
                    t.Margin = new Thickness(0, 0, 0, 6);   // 表の下端と横スクロールバーの間に余白
                    inner.Blocks.Add(t);
                    var box = new System.Windows.Controls.RichTextBox
                    {
                        Document = inner, IsReadOnly = true, IsDocumentEnabled = true,
                        Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0, 0, 0, 4),
                        Foreground = doc.Foreground, FontFamily = doc.FontFamily, FontSize = doc.FontSize,
                        HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                        Width = avail,
                    };
                    yield return new BlockUIContainer(box) { Margin = new Thickness(0, 4, 0, 8) };
                    break;
                }
                yield return t;
                break;
            }
            case HtmlBlock html:
            {
                var p = new Paragraph(new Run(html.Lines.ToString()) { Foreground = Dim }) { Margin = new Thickness(0, 2, 0, 6) };
                yield return p;
                break;
            }
            case ContainerBlock container:
                foreach (var child in container) foreach (var b in RenderBlock(child, doc)) yield return b;
                break;
            case LeafBlock leaf:
            {
                var p = new Paragraph();
                AddInlines(p.Inlines, leaf.Inline);
                yield return p;
                break;
            }
        }
    }

    /// <summary>ブロック内のテキストを素の文字列で取り出す（表の列幅計算用）。&lt;br&gt; は改行。</summary>
    private static string PlainText(Markdig.Syntax.Block block)
    {
        var sb = new System.Text.StringBuilder();
        void Walk(ContainerInline? c)
        {
            if (c is null) return;
            foreach (var i in c)
            {
                switch (i)
                {
                    case LiteralInline l: sb.Append(l.Content.ToString()); break;
                    case CodeInline code: sb.Append(code.Content); break;
                    case LineBreakInline: sb.Append('\n'); break;
                    case HtmlInline h when System.Text.RegularExpressions.Regex.IsMatch(h.Tag, @"^<br\s*/?>$", System.Text.RegularExpressions.RegexOptions.IgnoreCase): sb.Append('\n'); break;
                    case HtmlInline: break;
                    case ContainerInline ci: Walk(ci); break;
                    default: sb.Append(i.ToString()); break;
                }
            }
        }
        switch (block)
        {
            case LeafBlock leaf: Walk(leaf.Inline); break;
            case ContainerBlock cont: foreach (var ch in cont) { sb.Append(PlainText(ch)); sb.Append('\n'); } break;
        }
        return sb.ToString();
    }

    private static double MeasureText(string text, FlowDocument doc, bool bold)
    {
        if (text.Length == 0) return 0;
        try
        {
            var tf = new Typeface(doc.FontFamily, FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, doc.FontSize, Brushes.White, 1.0);
            return ft.WidthIncludingTrailingWhitespace;
        }
        catch { return text.Length * doc.FontSize * 0.6; }
    }

    private static void AddInlines(InlineCollection target, ContainerInline? container)
    {
        if (container is null) return;
        foreach (var inline in container)
        {
            if (inline is LiteralInline lit) { AddTextWithEmoji(target, lit.Content.ToString()); continue; }
            target.Add(RenderInline(inline));
        }
    }

    /// <summary>絵文字（🔴 ✅ など）はカラーで描く（Emoji.Wpf）。それ以外は通常の Run。</summary>
    private static void AddTextWithEmoji(InlineCollection target, string text)
    {
        if (text.Length == 0) return;
        System.Text.RegularExpressions.MatchCollection matches;
        try { matches = Emoji.Wpf.EmojiData.MatchOne.Matches(text); }
        catch { target.Add(new Run(text)); return; }
        int pos = 0;
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            if (m.Length == 0) continue;
            // 数字・#・* 単体（keycap の素）は絵文字扱いしない
            if (m.Length == 1 && (char.IsAsciiDigit(m.Value[0]) || m.Value[0] is '#' or '*')) continue;
            if (m.Index > pos) target.Add(new Run(text[pos..m.Index]));
            try { target.Add(new Emoji.Wpf.EmojiInline { Text = m.Value }); }
            catch { target.Add(new Run(m.Value)); }
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) target.Add(new Run(text[pos..]));
    }

    private static System.Windows.Documents.Inline RenderInline(Markdig.Syntax.Inlines.Inline inline)
    {
        switch (inline)
        {
            case LiteralInline lit: return new Run(lit.Content.ToString());
            case LineBreakInline br: return br.IsHard ? new LineBreak() : new Run(" ");
            case CodeInline code: return new Run(code.Content) { Background = CodeBg, Foreground = CodeFg };
            case EmphasisInline em:
            {
                var span = new Span();
                AddInlines(span.Inlines, em);
                if (em.DelimiterChar is '*' or '_') { if (em.DelimiterCount >= 2) span.FontWeight = FontWeights.Bold; else span.FontStyle = FontStyles.Italic; }
                else if (em.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
                return span;
            }
            case LinkInline link:
            {
                if (link.IsImage)
                {
                    var alt = new Span(); AddInlines(alt.Inlines, link);
                    var s = new Span { Foreground = Dim }; s.Inlines.Add(new Run("[画像: ")); s.Inlines.Add(alt); s.Inlines.Add(new Run($" {link.Url}]"));
                    return s;
                }
                var hl = new Hyperlink { Foreground = Accent, TextDecorations = TextDecorations.Underline };
                AddInlines(hl.Inlines, link);
                if (hl.Inlines.Count == 0) hl.Inlines.Add(new Run(link.Url ?? ""));
                var url = link.Url ?? "";
                hl.ToolTip = url;
                hl.Click += (_, _) => { try { FileOperations.ShellExecute(url); } catch { } };
                return hl;
            }
            case AutolinkInline auto:
            {
                var hl = new Hyperlink(new Run(auto.Url)) { Foreground = Accent };
                hl.Click += (_, _) => { try { FileOperations.ShellExecute(auto.Url); } catch { } };
                return hl;
            }
            case HtmlInline html:
            {
                // <br> は改行、それ以外のインライン HTML タグは表示しない（VS Code と同じ見え方に寄せる）
                var tag = html.Tag.Trim();
                if (System.Text.RegularExpressions.Regex.IsMatch(tag, @"^<br\s*/?>$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return new LineBreak();
                return new Run("");
            }
            case ContainerInline ci:
            {
                var span = new Span(); AddInlines(span.Inlines, ci); return span;
            }
            default: return new Run(inline.ToString() ?? "");
        }
    }
}
