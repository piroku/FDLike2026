using System.IO;

namespace FDLike2026.Services;

/// <summary>
/// Shift+W: 比較ツール（WinMerge など）の探索と起動。
/// 設定が空なら PATH と標準のインストール先から WinMergeU.exe を探す。
/// 設定に $L / $R が書かれていれば、その位置に左右のパスを入れる（他ツール用）。
/// </summary>
public static class DiffToolService
{
    private static readonly string[] WinMergeCandidates =
    {
        @"%ProgramFiles%\WinMerge\WinMergeU.exe",
        @"%ProgramFiles(x86)%\WinMerge\WinMergeU.exe",
        @"%LocalAppData%\Programs\WinMerge\WinMergeU.exe",
    };

    /// <summary>現在の設定値（F10 › 操作 › 比較ツール）。MainWindow が起動時と設定保存時に入れる。マクロからの参照用。</summary>
    public static string? Setting { get; set; }

    /// <summary>
    /// マクロ中の「WinMergeU.exe」「WinMerge」のようなコマンド名を、実在するパスへ置き換える。
    /// 設定の比較ツールが WinMerge を指していればそれ、無ければ PATH と標準インストール先。見つからなければそのまま返す。
    /// </summary>
    public static string ResolveWinMergeName(string head)
    {
        var bare = head.Trim('"');
        var fn = Path.GetFileName(bare);
        if (!(fn.Equals("WinMergeU.exe", StringComparison.OrdinalIgnoreCase) || fn.Equals("WinMerge.exe", StringComparison.OrdinalIgnoreCase)
              || fn.Equals("WinMergeU", StringComparison.OrdinalIgnoreCase) || fn.Equals("WinMerge", StringComparison.OrdinalIgnoreCase))) return head;
        if (bare.Contains('\\') && File.Exists(bare)) return head;   // もうフルパス
        string? found = null;
        if (!string.IsNullOrWhiteSpace(Setting))
        {
            var exe = FirstToken(Setting);
            if (Path.GetFileName(exe).StartsWith("WinMerge", StringComparison.OrdinalIgnoreCase) && ProcessLauncher.Resolve(exe) is { } p0) found = p0;
        }
        found ??= Resolve(null);
        return found is null ? head : "\"" + found + "\"";
    }

    /// <summary>設定文字列から実行可能なツール指定を返す。見つからなければ null。</summary>
    public static string? Resolve(string? setting)
    {
        if (!string.IsNullOrWhiteSpace(setting))
        {
            var s = setting.Trim();
            // $L / $R 付きのテンプレートはそのまま（先頭の exe が存在するかだけ確認）
            var exe = FirstToken(s);
            return ProcessLauncher.Resolve(exe) is not null ? s : null;
        }
        if (ProcessLauncher.Resolve("WinMergeU.exe") is { } p) return p;
        foreach (var c in WinMergeCandidates)
        {
            var full = Environment.ExpandEnvironmentVariables(c);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    public static void Launch(string tool, string left, string right, bool directories, string workDir)
    {
        string Q(string x) => "\"" + x + "\"";
        if (tool.Contains("$L") || tool.Contains("$R"))
        {
            var cmd = tool.Replace("$L", Q(left)).Replace("$R", Q(right));
            var exe = FirstToken(cmd);
            var args = cmd.Length > exe.Length ? cmd.Substring(IndexAfterFirstToken(cmd)).Trim() : "";
            ProcessLauncher.Start(exe, args, workDir);
            return;
        }
        var isWinMerge = Path.GetFileName(tool.Trim('"')).StartsWith("WinMerge", StringComparison.OrdinalIgnoreCase);
        var a = (directories && isWinMerge ? "/r " : "") + Q(left) + " " + Q(right);
        ProcessLauncher.Start(tool, a, workDir);
    }

    private static string FirstToken(string s)
    {
        s = s.TrimStart();
        if (s.StartsWith('"'))
        {
            int e = s.IndexOf('"', 1);
            return e > 0 ? s.Substring(1, e - 1) : s.Trim('"');
        }
        int sp = s.IndexOf(' ');
        return sp < 0 ? s : s[..sp];
    }

    private static int IndexAfterFirstToken(string s)
    {
        int lead = s.Length - s.TrimStart().Length;
        var t = s.TrimStart();
        if (t.StartsWith('"')) { int e = t.IndexOf('"', 1); return lead + (e > 0 ? e + 1 : t.Length); }
        int sp = t.IndexOf(' ');
        return lead + (sp < 0 ? t.Length : sp);
    }
}
