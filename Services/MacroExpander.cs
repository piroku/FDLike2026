using System.Diagnostics;
using System.IO;
using System.Text;
using FDLike2026.Models;

namespace FDLike2026.Services;

/// <summary>
/// WinFM2008 のコマンドマクロ（$P $C $X $T … の置換）と、その実行。
/// X: コマンドの実行、F2: コマンドマクロ、F4: 拡張子の関連づけ で共通に使う。
/// </summary>
public static class MacroExpander
{
    public sealed record Context(string Dir, FileEntry? Current, IReadOnlyList<FileEntry> Tagged, Func<string, string?>? Prompt,
                                 string? OtherDir = null, FileEntry? OtherCurrent = null);

    private static string Q(string s) => "\"" + s + "\"";

    /// <summary>$ マクロを展開する。$R が含まれていれば Prompt でパラメータを問い合わせる（キャンセルなら null）。</summary>
    public static string? Expand(string macro, Context c)
    {
        var now = DateTime.Now;
        var tagged = c.Tagged.Count > 0 ? c.Tagged : (c.Current is { IsParent: false } cur ? new[] { cur } : Array.Empty<FileEntry>());
        var sb = new StringBuilder();
        for (int i = 0; i < macro.Length; i++)
        {
            char ch = macro[i];
            if (ch != '$' || i + 1 >= macro.Length) { sb.Append(ch); continue; }
            char k = macro[++i];
            switch (k)
            {
                case '$': sb.Append('$'); break;
                case 'P': case 'p': sb.Append(c.Dir.TrimEnd('\\')); break;
                case 'D': sb.Append(Path.GetPathRoot(c.Dir)?.TrimEnd('\\') ?? ""); break;
                case 'C': case 'c': sb.Append(c.Current is { IsParent: false } ? c.Current.Name : ""); break;
                case 'X': case 'x': sb.Append(c.Current is { IsParent: false } ? Path.GetFileNameWithoutExtension(c.Current.Name) : ""); break;
                case 'T': case 'U': sb.Append(string.Join(" ", tagged.Select(t => t.Name.Contains(' ') ? Q(t.Name) : t.Name))); break;
                case 't': case 'u': sb.Append(string.Join(" ", tagged.Select(t => { var n = Path.GetFileNameWithoutExtension(t.Name); return n.Contains(' ') ? Q(n) : n; }))); break;
                case 'F': sb.Append(string.Join(" ", tagged.Select(t => Q(t.FullPath)))); break;   // FDLike 拡張: フルパス列
                case 'O':
                    {
                        // $O = 反対ペインのパス、$OC = 反対ペインのカーソル位置のファイル名（FDLike 拡張）
                        var other = c.OtherDir ?? c.Dir;
                        if (i + 1 < macro.Length && macro[i + 1] == 'C')
                        {
                            i++;
                            sb.Append(c.OtherCurrent is { IsParent: false } oc ? oc.Name : "");
                        }
                        else sb.Append(other.TrimEnd('\\'));
                        break;
                    }
                case 'Q': sb.Append('"'); break;
                case 'Z': sb.Append(','); break;
                case 'R':
                    {
                        var ans = c.Prompt?.Invoke("パラメータを入力してください");
                        if (ans is null) return null;
                        sb.Append(ans);
                        break;
                    }
                case 'S':
                    if (i + 1 < macro.Length)
                    {
                        char k2 = macro[++i];
                        if (k2 == 'N') sb.Append(tagged.Count);
                        else if (k2 == 's') sb.Append(tagged.Sum(t => t.Size));
                        else sb.Append('$').Append(k).Append(k2);
                    }
                    break;
                case 'Y':
                    if (i + 1 < macro.Length)
                    {
                        char k2 = macro[++i];
                        sb.Append(k2 switch
                        {
                            'Y' => now.ToString("yyyy"),
                            'y' => now.ToString("yy"),
                            'G' => JapaneseEra(now, true),
                            'g' => JapaneseEra(now, false),
                            'M' => now.ToString("MM"),
                            'D' => now.ToString("dd"),
                            'h' => now.ToString("HH"),
                            'm' => now.ToString("mm"),
                            's' => now.ToString("ss"),
                            _ => "$Y" + k2,
                        });
                    }
                    break;
                default: sb.Append('$').Append(k); break;
            }
        }
        return sb.ToString();
    }

    private static string JapaneseEra(DateTime d, bool withInitial)
    {
        var cal = new System.Globalization.JapaneseCalendar();
        int era = cal.GetEra(d), year = cal.GetYear(d);
        string initial = era switch { 5 => "R", 4 => "H", 3 => "S", 2 => "T", _ => "M" };
        return (withInitial ? initial : "") + year.ToString("00");
    }

    /// <summary>「タイトル,コマンド」形式ならタイトルとコマンドに分ける。</summary>
    public static (string Title, string Command) SplitTitle(string line)
    {
        var t = line.Trim();
        if (t.StartsWith('"')) return ("", t);           // 先頭が実行ファイルならタイトルなし
        int i = t.IndexOf(',');
        if (i > 0 && i < 40 && !t[..i].Contains('$') && !t[..i].Contains('\\')) return (t[..i].Trim(), t[(i + 1)..].Trim());
        return ("", t);
    }

    /// <summary>展開済みコマンドを実行する。';' があれば cmd で順に実行。先頭が "..." なら実行ファイル＋引数として直接起動。</summary>
    /// <summary>先頭のコマンドが WinMerge なら、インストール先のフルパスに置き換える（PATH に無い環境向け）。</summary>
    private static string FixWinMerge(string cmd)
    {
        string head, rest;
        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            if (end < 0) return cmd;
            head = cmd[..(end + 1)]; rest = cmd[(end + 1)..];
        }
        else
        {
            int sp = cmd.IndexOf(' ');
            head = sp < 0 ? cmd : cmd[..sp]; rest = sp < 0 ? "" : cmd[sp..];
        }
        var fixedHead = DiffToolService.ResolveWinMergeName(head);
        return ReferenceEquals(fixedHead, head) || fixedHead == head ? cmd : fixedHead + rest;
    }

    public static void Run(string expanded, string dir, bool keepWindow = false)
    {
        expanded = expanded.Trim();
        if (expanded.Length == 0) return;
        expanded = FixWinMerge(expanded);
        if (expanded.Contains(';'))
        {
            var joined = string.Join(" & ", expanded.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            Process.Start(new ProcessStartInfo("cmd.exe", (keepWindow ? "/k " : "/c ") + joined) { UseShellExecute = true, WorkingDirectory = dir });
            return;
        }
        if (expanded.StartsWith('"'))
        {
            int end = expanded.IndexOf('"', 1);
            if (end > 0)
            {
                var exe = expanded[1..end];
                var args = expanded[(end + 1)..].Trim();
                Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, WorkingDirectory = dir });
                return;
            }
        }
        // 実行ファイル名 + 引数（空白区切り）。見つからなければ cmd 経由
        int sp = expanded.IndexOf(' ');
        var head = sp < 0 ? expanded : expanded[..sp];
        var rest = sp < 0 ? "" : expanded[(sp + 1)..];
        if (head.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || File.Exists(head) || File.Exists(Path.Combine(dir, head)))
        {
            Process.Start(new ProcessStartInfo(head, rest) { UseShellExecute = true, WorkingDirectory = dir });
            return;
        }
        Process.Start(new ProcessStartInfo("cmd.exe", (keepWindow ? "/k " : "/c ") + expanded) { UseShellExecute = true, WorkingDirectory = dir });
    }
}
