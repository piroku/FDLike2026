using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;

namespace FDLike2026.Services;

/// <summary>
/// コードブロック用の軽量シンタックスハイライト（VS Code Dark+ 風の配色）。
/// 言語ごとのキーワード・コメント記法・文字列を正規表現で色分けする。厳密なパーサではない。
/// </summary>
public static class CodeHighlighter
{
    // VS Code Dark+ の配色
    public static readonly Brush Keyword  = Brush("#569CD6");
    public static readonly Brush Control  = Brush("#C586C0");
    public static readonly Brush Type     = Brush("#4EC9B0");
    public static readonly Brush Str      = Brush("#CE9178");
    public static readonly Brush Comment  = Brush("#6A9955");
    public static readonly Brush Number   = Brush("#B5CEA8");
    public static readonly Brush Func     = Brush("#DCDCAA");
    public static readonly Brush Plain    = Brush("#D4D4D4");
    public static readonly Brush Punct    = Brush("#D4D4D4");
    public static readonly Brush Attr     = Brush("#9CDCFE");
    public static readonly Brush Tag      = Brush("#569CD6");
    public static readonly Brush Background = Brush("#1E1E1E");

    private static Brush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private sealed record Lang(
        string[] Keywords, string[] Controls, string[] Types,
        string LineComment, string? BlockCommentStart, string? BlockCommentEnd,
        bool CaseInsensitive, bool Xml = false);

    private static readonly string[] VbKeywords =
    {
        "Private","Public","Friend","Protected","Function","Sub","End","Dim","As","Set","Let","Get","New","Nothing","True","False",
        "ByRef","ByVal","Optional","ParamArray","Const","Static","Global","Me","Property","Type","Enum","Declare","Lib","Alias",
        "Module","Class","Implements","Option","Explicit","Base","Compare","Attribute","Integer","Long","String","Boolean","Double",
        "Single","Variant","Object","Byte","Date","Currency","Collection","And","Or","Not","Xor","Mod","Is","Like","Erase","ReDim","Preserve",
        "WithEvents","Event","RaiseEvent","Null","Empty","Each","In","To","Step","Then","Shared","Inherits","Overrides","Imports","Namespace","Of","Handles","AddressOf","TypeOf","CType","DirectCast","Try","Catch","Finally","Throw","Using","Return",
    };
    private static readonly string[] VbControls =
    {
        "If","Else","ElseIf","Select","Case","For","Next","Do","Loop","While","Wend","Until","Exit","GoTo","Call","With","On","Error","Resume","Continue","Stop","Return",
    };

    private static readonly Dictionary<string, Lang> Langs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vb"] = new(VbKeywords, VbControls, Array.Empty<string>(), "'", null, null, true),
        ["vba"] = new(VbKeywords, VbControls, Array.Empty<string>(), "'", null, null, true),
        ["vb6"] = new(VbKeywords, VbControls, Array.Empty<string>(), "'", null, null, true),
        ["vbnet"] = new(VbKeywords, VbControls, Array.Empty<string>(), "'", null, null, true),
        ["basic"] = new(VbKeywords, VbControls, Array.Empty<string>(), "'", null, null, true),
        ["cs"] = new(
            new[] { "abstract","as","base","bool","byte","char","class","const","decimal","delegate","double","enum","event","explicit","extern","false","fixed","float","implicit","in","int","interface","internal","is","lock","long","namespace","new","null","object","operator","out","override","params","private","protected","public","readonly","ref","sbyte","sealed","short","sizeof","stackalloc","static","string","struct","this","true","typeof","uint","ulong","unchecked","unsafe","ushort","using","var","virtual","void","volatile","async","await","record","get","set","init","value","nameof","dynamic","where","partial","global" },
            new[] { "if","else","switch","case","default","for","foreach","while","do","break","continue","return","goto","throw","try","catch","finally","yield","when" },
            Array.Empty<string>(), "//", "/*", "*/", false),
        ["csharp"] = null!,
        ["js"] = new(
            new[] { "var","let","const","function","class","extends","new","this","super","null","undefined","true","false","typeof","instanceof","in","of","void","delete","async","await","static","get","set","import","export","from","default","as" },
            new[] { "if","else","switch","case","for","while","do","break","continue","return","throw","try","catch","finally","yield" },
            Array.Empty<string>(), "//", "/*", "*/", false),
        ["javascript"] = null!, ["ts"] = null!, ["typescript"] = null!,
        ["py"] = new(
            new[] { "def","class","lambda","import","from","as","global","nonlocal","None","True","False","and","or","not","in","is","with","self","print","async","await","del","assert" },
            new[] { "if","elif","else","for","while","break","continue","return","try","except","finally","raise","pass","yield" },
            Array.Empty<string>(), "#", null, null, false),
        ["python"] = null!,
        ["sql"] = new(
            new[] { "select","from","where","insert","into","values","update","set","delete","create","table","alter","drop","index","view","as","on","join","inner","left","right","outer","full","group","by","order","having","distinct","union","all","and","or","not","null","is","in","exists","between","like","case","when","then","else","end","begin","commit","rollback","declare","int","varchar","nvarchar","char","date","datetime","decimal","primary","key","foreign","references","default","constraint","with","top","limit","offset","asc","desc","count","sum","avg","min","max" },
            Array.Empty<string>(), Array.Empty<string>(), "--", "/*", "*/", true),
        ["ps"] = new(
            new[] { "function","param","return","begin","process","end","filter","in","foreach","true","false","null","Get-ChildItem","Get-Content","Set-Content","Write-Host","Write-Output","Select-Object","Where-Object","ForEach-Object","Start-Process","New-Item","Remove-Item","Copy-Item","Move-Item","Test-Path","Join-Path","Import-Module","Export-Csv","Import-Csv","Invoke-RestMethod","Invoke-WebRequest" },
            new[] { "if","elseif","else","switch","for","while","do","until","break","continue","try","catch","finally","throw","trap" },
            Array.Empty<string>(), "#", "<#", "#>", true),
        ["powershell"] = null!, ["pwsh"] = null!,
        ["sh"] = new(
            new[] { "echo","export","local","readonly","source","alias","cd","ls","cat","grep","sed","awk","set","unset","shift","exit","true","false","function" },
            new[] { "if","then","else","elif","fi","for","while","until","do","done","case","esac","in","break","continue","return" },
            Array.Empty<string>(), "#", null, null, false),
        ["bash"] = null!, ["shell"] = null!, ["zsh"] = null!,
        ["bat"] = new(
            new[] { "echo","set","setlocal","endlocal","enabledelayedexpansion","exist","not","errorlevel","defined","pause","exit","copy","move","del","mkdir","rmdir","cd","dir","type","start","pushd","popd","rem" },
            new[] { "if","else","for","in","do","goto","call","shift" },
            Array.Empty<string>(), "rem ", null, null, true),
        ["cmd"] = null!,
        ["json"] = new(new[] { "true","false","null" }, Array.Empty<string>(), Array.Empty<string>(), "//", null, null, false),
        ["xml"] = new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), "\u0001", "<!--", "-->", false, Xml: true),
        ["html"] = null!, ["xaml"] = null!,
        ["ini"] = new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), ";", null, null, false),
        ["yaml"] = new(new[] { "true","false","null","yes","no" }, Array.Empty<string>(), Array.Empty<string>(), "#", null, null, true),
        ["yml"] = null!,
        ["c"] = new(
            new[] { "auto","break","case","char","const","continue","default","do","double","else","enum","extern","float","for","goto","if","inline","int","long","register","restrict","return","short","signed","sizeof","static","struct","switch","typedef","union","unsigned","void","volatile","while","bool","true","false","nullptr","class","public","private","protected","virtual","override","template","typename","namespace","using","new","delete","this","std" },
            Array.Empty<string>(), Array.Empty<string>(), "//", "/*", "*/", false),
        ["cpp"] = null!, ["h"] = null!, ["java"] = null!, ["go"] = null!, ["rust"] = null!, ["rs"] = null!,
    };

    static CodeHighlighter()
    {
        // 別名は本体を共有
        void Alias(string alias, string of) => Langs[alias] = Langs[of];
        Alias("csharp", "cs"); Alias("javascript", "js"); Alias("ts", "js"); Alias("typescript", "js"); Alias("python", "py");
        Alias("powershell", "ps"); Alias("pwsh", "ps"); Alias("bash", "sh"); Alias("shell", "sh"); Alias("zsh", "sh"); Alias("cmd", "bat");
        Alias("html", "xml"); Alias("xaml", "xml"); Alias("yml", "yaml");
        Alias("cpp", "c"); Alias("h", "c"); Alias("java", "c"); Alias("go", "c"); Alias("rust", "c"); Alias("rs", "c");
    }

    /// <summary>info 文字列（```vb など）から言語を決める。不明なら null（色分けしない）。</summary>
    public static bool Supports(string? info) => info is not null && Langs.ContainsKey(info.Trim().Split(' ', '{')[0]);

    /// <summary>ファイルの拡張子から言語キーを返す（ビューアの色分け用）。対応しない拡張子は null。</summary>
    public static string? FromExtension(string? ext)
    {
        if (string.IsNullOrEmpty(ext)) return null;
        ext = ext.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "json" or "jsonc" => "json",
            "md" or "markdown" or "mdown" => "md",
            "cs" => "cs",
            "vb" or "vba" or "bas" or "frm" or "cls" or "ctl" or "dsr" => "vb",
            "js" or "mjs" or "cjs" or "ts" or "tsx" or "jsx" => "js",
            "py" or "pyw" => "py",
            "sql" => "sql",
            "ps1" or "psm1" or "psd1" => "ps",
            "sh" or "bash" or "zsh" => "sh",
            "bat" or "cmd" => "bat",
            "xml" or "xaml" or "html" or "htm" or "csproj" or "vbproj" or "props" or "targets" or "config" or "resx" or "svg" or "xsl" or "xslt" or "plist" or "manifest" => "xml",
            "ini" or "inf" or "cfg" or "conf" or "jig" or "editorconfig" or "gitconfig" => "ini",
            "yaml" or "yml" => "yaml",
            "c" or "h" or "cpp" or "cc" or "cxx" or "hpp" or "hh" or "java" or "go" or "rs" or "kt" or "swift" or "m" => "c",
            _ => null,
        };
    }

    /// <summary>言語指定が無いコードブロックの言語を中身から推定する。</summary>
    public static string? Guess(string code)
    {
        if (Regex.IsMatch(code, @"\b(End (Sub|Function|If)|Dim \w+ As|Private (Sub|Function)|ByVal|ByRef)\b")) return "vb";
        if (Regex.IsMatch(code, @"^\s*(<\?xml|<html|<[A-Za-z]+[^>]*>)", RegexOptions.Multiline)) return "xml";
        if (Regex.IsMatch(code, @"\b(using System|namespace \w+|public (static |sealed )?(class|void|int|string))\b")) return "cs";
        if (Regex.IsMatch(code, @"^\s*(def |import |from \w+ import|print\()", RegexOptions.Multiline)) return "py";
        if (Regex.IsMatch(code, @"\b(SELECT|INSERT|UPDATE|DELETE)\b[\s\S]*\b(FROM|INTO|SET|WHERE)\b", RegexOptions.IgnoreCase)) return "sql";
        if (Regex.IsMatch(code, @"\$\w+\s*=|Get-\w+|Write-Host|param\(")) return "ps";
        if (Regex.IsMatch(code, @"\b(function|const|let|var)\b.*(=>|\{)")) return "js";
        if (Regex.IsMatch(code, @"#include|std::|int main\(")) return "c";
        if (Regex.IsMatch(code, @"^\s*(#!/bin/|echo |export |if \[)", RegexOptions.Multiline)) return "sh";
        if (Regex.IsMatch(code, @"^\s*@echo|%\w+%|^\s*rem ", RegexOptions.Multiline | RegexOptions.IgnoreCase)) return "bat";
        return null;
    }

    /// <summary>Markdown のソースを VS Code 風に色分け（見出し・リスト記号・強調・インラインコード・リンク・表・フェンス・引用）。</summary>
    public static IEnumerable<Run> HighlightMarkdown(string code)
    {
        var heading = Brush("#569CD6");   // 見出し
        var marker  = Brush("#6796E6");   // - * 1. などのリスト記号
        var strong  = Brush("#569CD6");   // **強調**
        var inline  = Brush("#CE9178");   // `code`
        var linkTx  = Brush("#9CDCFE");   // [リンク文字]
        var linkUrl = Brush("#CE9178");   // (URL)
        var pipe    = Brush("#808080");   // 表の |
        var fence   = Brush("#6A9955");   // ``` と中身
        var quote   = Brush("#6A9955");   // > 引用
        var tag     = Brush("#569CD6");   // <br> などの HTML
        var inlineRe = new Regex(@"(?<code>`[^`\n]+`)|(?<strong>\*\*[^*\n]+\*\*|__[^_\n]+__)|(?<em>(?<![\w*])\*[^*\n]+\*(?!\w)|(?<!\w)_[^_\n]+_(?!\w))|(?<link>\[[^\]\n]*\])(?<url>\([^)\n]*\))?|(?<tag><[A-Za-z/][^>\n]*>)|(?<pipe>\|)", RegexOptions.Compiled);

        bool inFence = false;
        var lines = code.Replace("\r\n", "\n").Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            var line = lines[li];
            string nl = li < lines.Length - 1 ? "\n" : "";
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                inFence = !inFence;
                yield return new Run(line + nl) { Foreground = fence };
                continue;
            }
            if (inFence) { yield return new Run(line + nl) { Foreground = fence }; continue; }
            if (trimmed.StartsWith('#'))
            {
                yield return new Run(line + nl) { Foreground = heading, FontWeight = System.Windows.FontWeights.Bold };
                continue;
            }
            if (trimmed.StartsWith('>'))
            {
                yield return new Run(line + nl) { Foreground = quote };
                continue;
            }
            if (Regex.IsMatch(trimmed, @"^(-{3,}|\*{3,}|_{3,})\s*$"))
            {
                yield return new Run(line + nl) { Foreground = pipe };
                continue;
            }

            int pos = 0;
            // 行頭のリスト記号
            var m0 = Regex.Match(line, @"^(\s*)([-*+]|\d+[.)])(\s+)(\[[ xX]\]\s+)?");
            if (m0.Success)
            {
                yield return new Run(m0.Value) { Foreground = marker };
                pos = m0.Length;
            }
            foreach (Match m in inlineRe.Matches(line, pos))
            {
                if (m.Index > pos) yield return new Run(line[pos..m.Index]) { Foreground = Plain };
                if (m.Groups["code"].Success) yield return new Run(m.Value) { Foreground = inline };
                else if (m.Groups["strong"].Success) yield return new Run(m.Value) { Foreground = strong, FontWeight = System.Windows.FontWeights.Bold };
                else if (m.Groups["em"].Success) yield return new Run(m.Value) { Foreground = strong, FontStyle = System.Windows.FontStyles.Italic };
                else if (m.Groups["link"].Success)
                {
                    yield return new Run(m.Groups["link"].Value) { Foreground = linkTx };
                    if (m.Groups["url"].Success) yield return new Run(m.Groups["url"].Value) { Foreground = linkUrl, TextDecorations = System.Windows.TextDecorations.Underline };
                }
                else if (m.Groups["tag"].Success) yield return new Run(m.Value) { Foreground = tag };
                else if (m.Groups["pipe"].Success) yield return new Run(m.Value) { Foreground = pipe };
                pos = m.Index + m.Length;
            }
            if (pos < line.Length) yield return new Run(line[pos..]) { Foreground = Plain };
            if (nl.Length > 0) yield return new Run(nl) { Foreground = Plain };
        }
    }

    /// <summary>コードを色分けした Run の列にする。</summary>
    public static IEnumerable<Run> Highlight(string code, string? info)
    {
        var key = (info ?? "").Trim().Split(' ', '{')[0];
        if (key.Length == 0) key = Guess(code) ?? "";
        if (key is "md" or "markdown")
        {
            foreach (var r in HighlightMarkdown(code)) yield return r;
            yield break;
        }
        if (!Langs.TryGetValue(key, out var lang) || lang is null)
        {
            yield return new Run(code) { Foreground = Plain };
            yield break;
        }

        var ro = RegexOptions.Compiled | RegexOptions.Multiline | (lang.CaseInsensitive ? RegexOptions.IgnoreCase : 0);
        var parts = new List<string>();
        if (lang.BlockCommentStart is not null) parts.Add($"(?<bc>{Regex.Escape(lang.BlockCommentStart)}[\\s\\S]*?(?:{Regex.Escape(lang.BlockCommentEnd!)}|\\z))");
        if (lang.LineComment != "\u0001") parts.Add($"(?<lc>{Regex.Escape(lang.LineComment)}[^\\r\\n]*)");
        parts.Add("(?<str>@?\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"?|'(?:[^'\\\\\\r\\n]|\\\\.)*'|`[^`]*`)");
        if (lang.Xml)
        {
            parts.Add("(?<tag></?[A-Za-z_][\\w:.-]*|/?>)");
            parts.Add("(?<attr>[A-Za-z_][\\w:.-]*(?==))");
        }
        parts.Add("(?<num>\\b0x[0-9A-Fa-f]+\\b|\\b\\d+(?:\\.\\d+)?\\b)");
        parts.Add("(?<word>[A-Za-z_][\\w-]*)");
        var re = new Regex(string.Join("|", parts), ro);

        // VB 系は ' がコメント。文字列は " のみなので、str パターンの ' 部分を無効化
        bool vb = lang.LineComment == "'";
        var kw = new HashSet<string>(lang.Keywords, lang.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var ctl = new HashSet<string>(lang.Controls, lang.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        int pos = 0;
        foreach (Match m in re.Matches(code))
        {
            if (m.Index > pos) yield return new Run(code[pos..m.Index]) { Foreground = Plain };
            string text = m.Value;
            Brush brush;
            if (m.Groups["bc"].Success || m.Groups["lc"].Success) brush = Comment;
            else if (m.Groups["str"].Success)
            {
                if (vb && text.StartsWith('\''))
                {
                    // VB: ' から行末はコメント（str パターンに先に拾われた場合）
                    int eol = code.IndexOfAny(new[] { '\r', '\n' }, m.Index);
                    text = eol < 0 ? code[m.Index..] : code[m.Index..eol];
                    yield return new Run(text) { Foreground = Comment };
                    pos = m.Index + text.Length;
                    continue;
                }
                brush = Str;
                // JSON / YAML: 値ではなくキー（直後が :）は VS Code と同じ水色
                if (key is "json" or "yaml" or "yml")
                {
                    int k = m.Index + text.Length;
                    while (k < code.Length && (code[k] == ' ' || code[k] == '\t')) k++;
                    if (k < code.Length && code[k] == ':') brush = Attr;
                }
            }
            else if (m.Groups["tag"].Success) brush = Tag;
            else if (m.Groups["attr"].Success) brush = Attr;
            else if (m.Groups["num"].Success) brush = Number;
            else if (m.Groups["word"].Success)
            {
                if (ctl.Contains(text)) brush = Control;
                else if (kw.Contains(text)) brush = Keyword;
                else if (m.Index + text.Length < code.Length && code[m.Index + text.Length] == '(') brush = Func;   // 関数呼び出し
                else brush = Plain;
            }
            else brush = Plain;

            yield return new Run(text) { Foreground = brush };
            pos = m.Index + text.Length;
        }
        if (pos < code.Length) yield return new Run(code[pos..]) { Foreground = Plain };
    }
}
