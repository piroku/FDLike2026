using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace FDLike2026.Services;

/// <summary>
/// Office 文書 / PDF から「検索・表示用のテキスト」を取り出す（grep と ビューアで共用）。
///   .docx / .xlsx / .pptx … zip の中の XML を直接読む（ライブラリ不要）
///   .pdf                 … PdfPig で文字を取り出す（画像だけの PDF は空になる）
///   .doc / .xls / .ppt ほか … Windows の IFilter（Office が入っていればその場で読める）
/// 取り出したテキストは「── シート: 名前 ──」「── 2 ページ ──」のような区切り行入りで、
/// grep の行番号はこのテキストの行番号。ビューアも同じテキストを表示するので行が一致する。
/// </summary>
public static class DocumentText
{
    private static readonly HashSet<string> Ooxml = new(StringComparer.OrdinalIgnoreCase) { ".docx", ".docm", ".dotx", ".xlsx", ".xlsm", ".xltx", ".pptx", ".pptm", ".potx" };
    private static readonly HashSet<string> Legacy = new(StringComparer.OrdinalIgnoreCase) { ".doc", ".dot", ".xls", ".xlt", ".ppt", ".pot", ".pps", ".rtf", ".odt", ".ods", ".odp", ".one", ".msg", ".vsd", ".vsdx", ".mpp", ".pub", ".xps", ".oxps" };

    /// <summary>grep / ビューアが「中身を取り出して扱う」拡張子か。</summary>
    public static bool IsDocument(string ext) => Ooxml.Contains(ext) || Legacy.Contains(ext) || ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>マスクが空のときに grep が対象に含める拡張子の一覧（表示用）。</summary>
    public static string Summary => "docx xlsx pptx pdf doc xls ppt rtf odt ods odp msg vsd ほか（旧形式は IFilter が入っていれば）";

    /// <summary>テキストを取り出す。読めない・空なら null。例外は投げない。</summary>
    public static string? Extract(string path)
    {
        var ext = Path.GetExtension(path);
        try
        {
            string? text = null;
            if (Ooxml.Contains(ext)) text = ExtractOoxml(path, ext);
            else if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) text = ExtractPdf(path);
            else if (Legacy.Contains(ext)) text = IFilterReader.Extract(path);
            if (text is null) return null;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch { return null; }
    }

    /// <summary>ビューアのヘッダー用: どの方法で取り出したかの短い説明。</summary>
    public static string MethodLabel(string ext)
    {
        if (Ooxml.Contains(ext)) return "Office 文書から抽出したテキスト";
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return "PDF から抽出したテキスト";
        return "IFilter で抽出したテキスト";
    }

    // ============================================================ OOXML ============================================================

    private static string? ExtractOoxml(string path, string ext)
    {
        using var zip = ZipFile.OpenRead(path);
        var e = ext.ToLowerInvariant();
        if (e.StartsWith(".doc") || e.StartsWith(".dot")) return ExtractDocx(zip);
        if (e.StartsWith(".xls") || e.StartsWith(".xlt")) return ExtractXlsx(zip);
        if (e.StartsWith(".ppt") || e.StartsWith(".pot")) return ExtractPptx(zip);
        return null;
    }

    private static readonly XmlReaderSettings XmlSettings = new() { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true, IgnoreProcessingInstructions = true, XmlResolver = null };

    /// <summary>Word: 本文 → ヘッダー → フッター → 脚注 の順。段落ごとに 1 行。</summary>
    private static string ExtractDocx(ZipArchive zip)
    {
        var sb = new StringBuilder();
        void Part(string name, string? label)
        {
            var entry = zip.GetEntry(name);
            if (entry is null) return;
            if (label is not null) sb.Append("── ").Append(label).Append(" ──\n");
            using var s = entry.Open();
            ReadParagraphs(s, "w", sb);
        }
        Part("word/document.xml", null);
        foreach (var en in zip.Entries.Where(x => Regex.IsMatch(x.FullName, @"^word/header\d*\.xml$")).OrderBy(x => x.FullName)) Part(en.FullName, "ヘッダー");
        foreach (var en in zip.Entries.Where(x => Regex.IsMatch(x.FullName, @"^word/footer\d*\.xml$")).OrderBy(x => x.FullName)) Part(en.FullName, "フッター");
        Part("word/footnotes.xml", "脚注");
        Part("word/endnotes.xml", "文末脚注");
        Part("word/comments.xml", "コメント");
        return sb.ToString();
    }

    /// <summary>w:p / a:p を行、w:t / a:t を文字、w:tab をタブ、w:br を改行として読む。</summary>
    private static void ReadParagraphs(Stream s, string prefix, StringBuilder sb)
    {
        using var r = XmlReader.Create(s, XmlSettings);
        var line = new StringBuilder();
        bool any = false, stay = false;
        // ReadElementContentAsString は次のノードまで進むので、そのときは Read() を飛ばす（</p> や </c> を読み落とさないため）
        while (stay || r.Read())
        {
            stay = false;
            if (r.NodeType == XmlNodeType.Element)
            {
                switch (r.LocalName)
                {
                    case "p": any = true; break;
                    case "t": if (!r.IsEmptyElement) { line.Append(r.ReadElementContentAsString()); stay = true; } break;
                    case "tab": line.Append('\t'); break;
                    case "br": case "cr": line.Append('\n'); break;
                }
            }
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "p")
            {
                sb.Append(line).Append('\n');
                line.Clear();
            }
        }
        if (line.Length > 0 || !any) sb.Append(line).Append('\n');
    }

    /// <summary>Excel: シートごとに「── シート: 名前 ──」、1 行 = 1 行、セルはタブ区切り。</summary>
    private static string ExtractXlsx(ZipArchive zip)
    {
        // 共有文字列
        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is { } ss)
        {
            using var s = ss.Open();
            using var r = XmlReader.Create(s, XmlSettings);
            StringBuilder? cur = null; bool stay = false, inRph = false;
            while (stay || r.Read())
            {
                stay = false;
                if (r.NodeType == XmlNodeType.Element)
                {
                    if (r.LocalName == "si") { cur = new StringBuilder(); inRph = false; }
                    else if (r.LocalName == "rPh") inRph = !r.IsEmptyElement;   // ふりがな（PHONETIC）は本文ではないので除く
                    else if (r.LocalName == "t" && cur is not null && !inRph && !r.IsEmptyElement) { cur.Append(r.ReadElementContentAsString()); stay = true; }
                }
                else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "rPh") inRph = false;
                else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "si" && cur is not null) { shared.Add(cur.ToString()); cur = null; }
            }
        }
        // シート名と実体の対応
        var rels = new Dictionary<string, string>();
        if (zip.GetEntry("xl/_rels/workbook.xml.rels") is { } re)
        {
            using var s = re.Open();
            using var r = XmlReader.Create(s, XmlSettings);
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship")
                {
                    var id = r.GetAttribute("Id"); var target = r.GetAttribute("Target");
                    if (id is not null && target is not null) rels[id] = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                }
        }
        var sheets = new List<(string name, string part)>();
        if (zip.GetEntry("xl/workbook.xml") is { } wb)
        {
            using var s = wb.Open();
            using var r = XmlReader.Create(s, XmlSettings);
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "sheet")
                {
                    var name = r.GetAttribute("name") ?? "";
                    var rid = r.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships") ?? "";
                    if (rels.TryGetValue(rid, out var part)) sheets.Add((name, part));
                }
        }
        if (sheets.Count == 0)
            sheets.AddRange(zip.Entries.Where(x => Regex.IsMatch(x.FullName, @"^xl/worksheets/sheet\d+\.xml$")).OrderBy(x => x.FullName, StringComparer.Ordinal).Select(x => (Path.GetFileNameWithoutExtension(x.FullName), x.FullName)));

        var sb = new StringBuilder();
        foreach (var (name, part) in sheets)
        {
            var entry = zip.GetEntry(part);
            if (entry is null) continue;
            sb.Append("── シート: ").Append(name).Append(" ──\n");
            using var s = entry.Open();
            using var r = XmlReader.Create(s, XmlSettings);
            var cells = new List<string>();
            string? type = null; var cell = new StringBuilder(); bool inIs = false, stay = false;
            while (stay || r.Read())
            {
                stay = false;
                if (r.NodeType == XmlNodeType.Element)
                {
                    switch (r.LocalName)
                    {
                        case "row": cells.Clear(); break;
                        case "c": type = r.GetAttribute("t"); cell.Clear(); break;
                        case "is": inIs = true; break;
                        case "t": if (inIs && !r.IsEmptyElement) { cell.Append(r.ReadElementContentAsString()); stay = true; } break;
                        case "v":
                            if (!r.IsEmptyElement)
                            {
                                var v = r.ReadElementContentAsString(); stay = true;
                                if (type == "s" && int.TryParse(v, out var idx) && idx >= 0 && idx < shared.Count) cell.Append(shared[idx]);
                                else if (type == "b") cell.Append(v == "1" ? "TRUE" : "FALSE");
                                else cell.Append(v);
                            }
                            break;
                    }
                }
                else if (r.NodeType == XmlNodeType.EndElement)
                {
                    switch (r.LocalName)
                    {
                        case "is": inIs = false; break;
                        case "c": if (cell.Length > 0) cells.Add(cell.ToString()); break;
                        case "row": sb.Append(string.Join('\t', cells)).Append('\n'); cells.Clear(); break;
                    }
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>PowerPoint: スライド番号順に「── スライド n ──」、段落ごとに 1 行。ノートも続けて。</summary>
    private static string ExtractPptx(ZipArchive zip)
    {
        var sb = new StringBuilder();
        static int Num(string n) { var m = Regex.Match(n, @"(\d+)\.xml$"); return m.Success ? int.Parse(m.Groups[1].Value) : 0; }
        var slides = zip.Entries.Where(x => Regex.IsMatch(x.FullName, @"^ppt/slides/slide\d+\.xml$")).OrderBy(x => Num(x.FullName)).ToList();
        foreach (var sl in slides)
        {
            sb.Append("── スライド ").Append(Num(sl.FullName)).Append(" ──\n");
            using var s = sl.Open();
            ReadParagraphs(s, "a", sb);
        }
        var notes = zip.Entries.Where(x => Regex.IsMatch(x.FullName, @"^ppt/notesSlides/notesSlide\d+\.xml$")).OrderBy(x => Num(x.FullName)).ToList();
        foreach (var n in notes)
        {
            sb.Append("── ノート ").Append(Num(n.FullName)).Append(" ──\n");
            using var s = n.Open();
            ReadParagraphs(s, "a", sb);
        }
        return sb.ToString();
    }

    // ============================================================ PDF ============================================================

    private static string ExtractPdf(string path)
    {
        var sb = new StringBuilder();
        using var doc = UglyToad.PdfPig.PdfDocument.Open(path, new UglyToad.PdfPig.ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        foreach (var page in doc.GetPages())
        {
            sb.Append("── ").Append(page.Number).Append(" ページ ──\n");
            string t;
            try { t = UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page); }
            catch { t = page.Text; }
            sb.Append(t.TrimEnd()).Append('\n');
        }
        return sb.ToString();
    }
}
