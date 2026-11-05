using System.Diagnostics;
using System.IO;

namespace FDLike2026.Services;

/// <summary>
/// git 管理下のフォルダで、各ファイルの状態（M / U / A / D / R / !）とフォルダの「配下に変更あり」を取る。
/// git.exe が無い PC や管理外のフォルダでは空を返す。呼び出し側は結果をファイル一覧の git 列に反映する。
/// </summary>
public static class GitStatusService
{
    public sealed class Result
    {
        /// <summary>フルパス（小文字比較）→ 状態 1 文字。</summary>
        public Dictionary<string, char> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>配下に変更（M/A/D/R/U）を含むフォルダのフルパス。</summary>
        public HashSet<string> DirtyDirs { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>フォルダごと無視 / 未追跡のフォルダ。</summary>
        public Dictionary<string, char> Dirs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string Root { get; set; } = "";
    }

    private static bool? _hasGit;
    private static readonly Dictionary<string, (DateTime at, Result res)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Lock = new();

    /// <summary>git.exe が使えるか（初回だけ調べる）。</summary>
    public static bool HasGit
    {
        get
        {
            if (_hasGit is { } v) return v;
            try
            {
                var p = Run("", "--version", 3000);
                _hasGit = p is not null && p.StartsWith("git version");
            }
            catch { _hasGit = false; }
            return _hasGit.Value;
        }
    }

    /// <summary>キャッシュを捨てる（F5 や自動更新のとき）。</summary>
    public static void Invalidate() { lock (Lock) Cache.Clear(); }

    /// <summary>dir が git 管理下ならその状態を返す。管理外 / git 無しなら null。バックグラウンドで呼ぶこと。</summary>
    public static Result? Get(string dir, bool useCache = true)
    {
        if (!HasGit) return null;
        if (!Directory.Exists(dir)) return null;
        if (Path.GetFileName(dir).Equals(".git", StringComparison.OrdinalIgnoreCase)) return null;

        string? root = Run(dir, "rev-parse --show-toplevel", 4000)?.Trim();
        if (string.IsNullOrEmpty(root) || root.StartsWith("fatal")) return null;
        root = root.Replace('/', Path.DirectorySeparatorChar);

        lock (Lock)
        {
            if (useCache && Cache.TryGetValue(root, out var c) && (DateTime.Now - c.at).TotalSeconds < 5) return c.res;
        }

        // 無視されたファイルも含め、リポジトリ全体を 1 回で取る（サブフォルダの ● 判定に使う）
        var text = Run(root, "status --porcelain=v1 --ignored -z --untracked-files=normal", 15000);
        if (text is null) return null;

        var res = new Result { Root = root };
        var items = text.Split('\0');
        for (int i = 0; i < items.Length; i++)
        {
            var raw = items[i];
            if (raw.Length < 4) continue;
            char x = raw[0], y = raw[1];
            var rel = raw[3..];
            // リネーム / コピーは -z では "R  new\0old" と 2 要素。old は読み飛ばす
            if (x is 'R' or 'C' || y is 'R' or 'C') i++;
            bool isDir = rel.EndsWith('/');
            var full = Path.GetFullPath(Path.Combine(root, rel.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar)));

            char st;
            if (x == '?' ) st = 'U';
            else if (x == '!') st = '!';
            else if (y != ' ') st = y;          // 作業ツリーの状態を優先（M, D）
            else st = x;                         // ステージ済み（A, M, D, R）
            if (st == 'C') st = 'A';

            if (isDir) res.Dirs[full] = st;
            else res.Files[full] = st;

            if (st != '!')
            {
                // 親フォルダをルートまで dirty に
                var p = isDir ? full : Path.GetDirectoryName(full);
                while (p is not null && p.Length >= root.Length)
                {
                    res.DirtyDirs.Add(p);
                    if (string.Equals(p, root, StringComparison.OrdinalIgnoreCase)) break;
                    p = Path.GetDirectoryName(p);
                }
            }
        }
        lock (Lock) Cache[root] = (DateTime.Now, res);
        return res;
    }

    /// <summary>一覧の 1 行に出す文字とキー。files: "M" / "U" / "A" / "D" / "R" / "!"、フォルダ: "●"（変更あり）または "!"/"U"。</summary>
    public static (string text, string key) For(Result res, string fullPath, bool isDirectory)
    {
        if (isDirectory)
        {
            if (res.Dirs.TryGetValue(fullPath, out var d)) return d == '!' ? ("!", "Ignored") : ("U", "Untracked");
            if (res.DirtyDirs.Contains(fullPath)) return ("●", "Modified");
            // 無視フォルダの配下
            return ("", "");
        }
        if (res.Files.TryGetValue(fullPath, out var s))
            return s switch
            {
                'M' => ("M", "Modified"),
                'A' => ("A", "Added"),
                'D' => ("D", "Deleted"),
                'R' => ("R", "Added"),
                'U' => ("U", "Untracked"),
                '!' => ("!", "Ignored"),
                _ => (s.ToString(), "Modified"),
            };
        // 未追跡フォルダ / 無視フォルダの中身
        var dir = Path.GetDirectoryName(fullPath);
        while (dir is not null && dir.Length >= res.Root.Length)
        {
            if (res.Dirs.TryGetValue(dir, out var d)) return d == '!' ? ("!", "Ignored") : ("U", "Untracked");
            if (string.Equals(dir, res.Root, StringComparison.OrdinalIgnoreCase)) break;
            dir = Path.GetDirectoryName(dir);
        }
        return ("", "");
    }

    private static string? Run(string dir, string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            if (dir.Length > 0) psi.WorkingDirectory = dir;
            psi.Environment["LC_ALL"] = "C";
            psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";   // index.lock を作らない
            using var p = Process.Start(psi);
            if (p is null) return null;
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return null; }
            var output = outTask.Result;
            if (p.ExitCode != 0) return output.Length > 0 ? output : "fatal";
            return output;
        }
        catch { return null; }
    }
}
