using System.IO;
using System.IO.Compression;
using System.Text;
using FDLike2026.Models;

namespace FDLike2026.Services;

/// <summary>P: 圧縮 / U: 解凍（zip）。.NET 標準の ZipFile を使い、外部 DLL は不要。</summary>
public static class ArchiveService
{
    public static bool IsArchive(string path)
        => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>日本語ファイル名が化けないよう、Shift_JIS で書かれた古い zip も読めるエンコーディングを返す。</summary>
    private static Encoding LegacyEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    /// <summary>
    /// items を zipPath に圧縮する。フォルダは再帰。
    /// progress には (処理済みファイル数, 総数, いま処理中の名前)。
    /// </summary>
    public static void Pack(IReadOnlyList<FileEntry> items, string zipPath, CompressionLevel level,
                            IProgress<(int done, int total, string name)>? progress, CancellationToken token)
    {
        // 対象ファイルを数える
        var files = new List<(string full, string entry)>();
        var dirs = new List<string>();
        foreach (var it in items)
        {
            if (it.IsDirectory)
            {
                var root = it.FullPath.TrimEnd('\\');
                var baseName = Path.GetFileName(root);
                dirs.Add(baseName + "/");
                var opt = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = 0 };
                foreach (var d in new DirectoryInfo(root).EnumerateDirectories("*", opt))
                    dirs.Add(baseName + "/" + Path.GetRelativePath(root, d.FullName).Replace('\\', '/') + "/");
                foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", opt))
                    files.Add((f.FullName, baseName + "/" + Path.GetRelativePath(root, f.FullName).Replace('\\', '/')));
            }
            else files.Add((it.FullPath, it.Name));
        }

        var tmp = zipPath + ".tmp~";
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create, Encoding.UTF8))
            {
                foreach (var d in dirs.Distinct()) zip.CreateEntry(d);
                int n = 0;
                foreach (var (full, entry) in files)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report((n, files.Count, entry));
                    // CreateEntryFromFile は途中で中止できないので、自前でコピーして大きなファイルでも中止が効くようにする
                    var fi = new FileInfo(full);
                    var ze = zip.CreateEntry(entry, level);
                    ze.LastWriteTime = fi.LastWriteTime;
                    using (var src = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16))
                    using (var dst = ze.Open())
                        CopyWithProgress(src, dst, fi.Length, entry, n, files.Count, progress, token);
                    n++;
                }
                progress?.Report((files.Count, files.Count, ""));
            }
            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(tmp, zipPath);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }
    }

    /// <summary>zipPath を destDir に展開する。overwrite=false のときは既存ファイルをスキップ。</summary>
    public static void Unpack(string zipPath, string destDir, bool overwrite,
                              IProgress<(int done, int total, string name)>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(destDir);
        using var zip = OpenRead(zipPath);
        int total = zip.Entries.Count, n = 0;
        var destFull = Path.GetFullPath(destDir + Path.DirectorySeparatorChar);
        foreach (var e in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report((n++, total, e.FullName));
            var target = Path.GetFullPath(Path.Combine(destDir, e.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(destFull, StringComparison.OrdinalIgnoreCase)) continue;   // Zip Slip 対策
            if (e.FullName.EndsWith('/') || e.Name.Length == 0) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target) && !overwrite) continue;
            // ExtractToFile は途中で中止できないので、自前でコピーして大きなファイルでも中止が効くようにする
            try
            {
                using (var src = e.Open())
                using (var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                    CopyWithProgress(src, dst, e.Length, e.FullName, n - 1, total, progress, token);
                File.SetLastWriteTime(target, e.LastWriteTime.DateTime);
            }
            catch
            {
                try { File.Delete(target); } catch { }   // 中途半端なファイルを残さない
                throw;
            }
        }
        progress?.Report((total, total, ""));
    }

    /// <summary>
    /// 1 MB ごとに中止要求を確認しながらコピーし、大きなファイルは「名前 (済み MB / 全体 MB)」で進捗を出す。
    /// </summary>
    private static void CopyWithProgress(Stream src, Stream dst, long length, string name, int done, int total,
                                         IProgress<(int done, int total, string name)>? progress, CancellationToken token)
    {
        var buf = new byte[1 << 20];
        long copied = 0, lastReport = 0;
        int read;
        while ((read = src.Read(buf, 0, buf.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            dst.Write(buf, 0, read);
            copied += read;
            if (length >= 8L << 20 && copied - lastReport >= 4L << 20)   // 8 MB 以上のファイルは 4 MB ごとに表示を更新
            {
                lastReport = copied;
                progress?.Report((done, total, $"{name}  ({copied >> 20:#,0} / {length >> 20:#,0} MB)"));
            }
        }
    }

    /// <summary>UTF-8 フラグの無いエントリ名は Shift_JIS として読む（Windows 標準の zip 互換）。</summary>
    public static ZipArchive OpenRead(string zipPath)
        => ZipFile.Open(zipPath, ZipArchiveMode.Read, LegacyEncoding());

    /// <summary>V: 書庫の中身を一覧テキストにする。</summary>
    public static string ListText(string zipPath)
    {
        using var zip = OpenRead(zipPath);
        var sb = new StringBuilder();
        long total = 0, packed = 0;
        sb.AppendLine($"{Path.GetFileName(zipPath)}  ({zip.Entries.Count:#,0} エントリ)");
        sb.AppendLine();
        sb.AppendLine($"{"サイズ",14}  {"圧縮後",14}  {"更新日時",16}  名前");
        sb.AppendLine(new string('-', 80));
        foreach (var e in zip.Entries)
        {
            total += e.Length; packed += e.CompressedLength;
            sb.AppendLine($"{e.Length,14:#,0}  {e.CompressedLength,14:#,0}  {e.LastWriteTime:yyyy/MM/dd HH:mm}  {e.FullName}");
        }
        sb.AppendLine(new string('-', 80));
        sb.AppendLine($"{total,14:#,0}  {packed,14:#,0}  合計  圧縮率 {(total > 0 ? 100.0 * packed / total : 0):0.0}%");
        return sb.ToString();
    }

    /// <summary>圧縮ファイル名の既定値: 1 件ならその名前、複数ならフォルダ名。</summary>
    public static string DefaultZipName(IReadOnlyList<FileEntry> items, string dir)
    {
        string stem = items.Count == 1
            ? (items[0].IsDirectory ? items[0].Name : Path.GetFileNameWithoutExtension(items[0].Name))
            : Path.GetFileName(dir.TrimEnd('\\'));
        if (string.IsNullOrEmpty(stem)) stem = "archive";
        return stem + ".zip";
    }
}
