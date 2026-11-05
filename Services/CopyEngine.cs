using System.IO;
using System.Text.RegularExpressions;
using FDLike2026.Models;

namespace FDLike2026.Services;

public enum OverwriteMode { Ask, Overwrite, Newer, Skip, RenameNew, Cancel }

public sealed class CopyOptions
{
    public string DestDir { get; set; } = "";
    public bool Move { get; set; }
    public OverwriteMode Overwrite { get; set; } = OverwriteMode.Ask;
    public bool UpdateOnly { get; set; }           // 既存のファイルのみ（アップデート）
    public bool ClearReadOnly { get; set; }        // 書き込み禁止属性を外して複写
    public bool StructureOnly { get; set; }        // フォルダ構造のみ
    public string? RenameTo { get; set; }          // 名前を変えて複写（1 件のとき）
    public Dictionary<string, string>? Renames { get; set; }   // 複写元フルパス → 新しい名前（同じフォルダへの複写で「名前の変更」をしたとき）
    public string IncludeFilter { get; set; } = "";
    public string ExcludeFilter { get; set; } = "";
}

/// <summary>
/// WinFM2008 の「ファイルの複写と移動」相当の自前エンジン。
/// 上書き時の方法選択、更新のみ、フィルタ、名前を変えて複写、進捗、中止に対応。
/// </summary>
public static class CopyEngine
{
    public sealed record Job(string Src, string Dst, bool IsDir);

    /// <summary>上書き確認。戻り値は (決定, 以後同じ)。UI スレッドで呼ばれる前提で、呼び出し側が Dispatcher で包む。</summary>
    public delegate (OverwriteMode mode, bool applyAll) AskOverwrite(string src, string dst);

    public static void Run(IReadOnlyList<FileEntry> items, CopyOptions o,
                           IProgress<(int done, int total, string name)>? progress, AskOverwrite ask, CancellationToken token)
    {
        var include = BuildFilter(o.IncludeFilter);
        var exclude = BuildFilter(o.ExcludeFilter);
        var jobs = new List<Job>();
        var srcDirs = new List<string>();

        foreach (var it in items)
        {
            var name = o.Renames is not null && o.Renames.TryGetValue(it.FullPath, out var rn) ? rn
                     : items.Count == 1 && !string.IsNullOrWhiteSpace(o.RenameTo) ? o.RenameTo! : it.Name;
            var dst = Path.Combine(o.DestDir, name);
            if (it.IsDirectory)
            {
                var root = it.FullPath.TrimEnd('\\');
                jobs.Add(new Job(root, dst, true));
                srcDirs.Add(root);
                var opt = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = 0 };
                foreach (var d in new DirectoryInfo(root).EnumerateDirectories("*", opt))
                {
                    jobs.Add(new Job(d.FullName, Path.Combine(dst, Path.GetRelativePath(root, d.FullName)), true));
                    srcDirs.Add(d.FullName);
                }
                if (!o.StructureOnly)
                    foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", opt))
                    {
                        if (include is not null && !include.Any(r => r.IsMatch(f.Name))) continue;
                        if (exclude is not null && exclude.Any(r => r.IsMatch(f.Name))) continue;
                        jobs.Add(new Job(f.FullName, Path.Combine(dst, Path.GetRelativePath(root, f.FullName)), false));
                    }
            }
            else if (!o.StructureOnly)
            {
                jobs.Add(new Job(it.FullPath, dst, false));
            }
        }

        // 自分自身の中への複写を防ぐ
        foreach (var d in srcDirs)
            if (Path.GetFullPath(o.DestDir + "\\").StartsWith(Path.GetFullPath(d + "\\"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("フォルダを自分自身（またはそのサブフォルダ）へ複写・移動することはできません。");

        OverwriteMode? sticky = o.Overwrite == OverwriteMode.Ask ? null : o.Overwrite;
        int total = jobs.Count(j => !j.IsDir), done = 0;

        foreach (var j in jobs)
        {
            token.ThrowIfCancellationRequested();
            if (j.IsDir) { Directory.CreateDirectory(j.Dst); continue; }
            progress?.Report((done, total, Path.GetFileName(j.Src)));

            var dst = j.Dst;
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            bool exists = File.Exists(dst);
            if (o.UpdateOnly && !exists) { done++; continue; }

            if (exists)
            {
                var mode = sticky ?? OverwriteMode.Ask;
                if (mode == OverwriteMode.Ask)
                {
                    var (m, all) = ask(j.Src, dst);
                    mode = m;
                    if (all) sticky = m;
                }
                switch (mode)
                {
                    case OverwriteMode.Cancel: throw new OperationCanceledException();
                    case OverwriteMode.Skip: done++; continue;
                    case OverwriteMode.Newer:
                        if (File.GetLastWriteTimeUtc(j.Src) <= File.GetLastWriteTimeUtc(dst)) { done++; continue; }
                        break;
                    case OverwriteMode.RenameNew:
                        dst = UniqueName(dst);
                        exists = false;
                        break;
                }
                if (exists)
                {
                    var a = File.GetAttributes(dst);
                    if (a.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(dst, a & ~FileAttributes.ReadOnly);
                }
            }

            if (o.Move)
            {
                if (exists) File.Delete(dst);
                File.Move(j.Src, dst);   // 別ドライブでも .NET がコピー＋削除してくれる
            }
            else
            {
                File.Copy(j.Src, dst, true);
            }
            if (o.ClearReadOnly)
            {
                var a = File.GetAttributes(dst);
                if (a.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(dst, a & ~FileAttributes.ReadOnly);
            }
            done++;
        }

        // 移動なら空になった元フォルダを深い順に削除
        if (o.Move)
            foreach (var d in srcDirs.OrderByDescending(d => d.Length))
                try { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }

        progress?.Report((total, total, ""));
    }

    public static string UniqueName(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p) && !Directory.Exists(p)) return p;
        }
    }

    private static List<Regex>? BuildFilter(string text)
    {
        var parts = text.Split(new[] { ' ', '　', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        return parts.Select(p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase)).ToList();
    }
}
