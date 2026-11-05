using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using FDLike2026.Models;

namespace FDLike2026.Services;

/// <summary>
/// 複写 / 移動 / 削除 / 属性変更 / 実行。
/// Windows シェルと同じ進捗ダイアログ・ごみ箱を使うため Microsoft.VisualBasic.FileIO.FileSystem を利用する。
/// </summary>
public static class FileOperations
{
    /// <summary>パス文字列（ドロップされたもの）から FileEntry を作る。存在しないものは除く。</summary>
    public static List<FileEntry> FromPaths(IEnumerable<string> paths)
    {
        var list = new List<FileEntry>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p)) list.Add(new FileEntry(new DirectoryInfo(p)));
            else if (File.Exists(p)) list.Add(new FileEntry(new FileInfo(p)));
        }
        return list;
    }

    public static void Copy(IEnumerable<FileEntry> items, string destDir, IReadOnlyDictionary<string, string>? renames = null)
    {
        Directory.CreateDirectory(destDir);
        foreach (var e in items)
        {
            var dest = Path.Combine(destDir, renames is not null && renames.TryGetValue(e.FullPath, out var rn) ? rn : e.Name);
            if (string.Equals(e.FullPath, dest, StringComparison.OrdinalIgnoreCase)) continue;
            if (e.IsDirectory)
                FileSystem.CopyDirectory(e.FullPath, dest, UIOption.AllDialogs, UICancelOption.ThrowException);
            else
                FileSystem.CopyFile(e.FullPath, dest, UIOption.AllDialogs, UICancelOption.ThrowException);
        }
    }

    public static void Move(IEnumerable<FileEntry> items, string destDir, IReadOnlyDictionary<string, string>? renames = null)
    {
        Directory.CreateDirectory(destDir);
        foreach (var e in items)
        {
            var dest = Path.Combine(destDir, renames is not null && renames.TryGetValue(e.FullPath, out var rn) ? rn : e.Name);
            if (string.Equals(e.FullPath, dest, StringComparison.OrdinalIgnoreCase)) continue;
            if (e.IsDirectory)
                FileSystem.MoveDirectory(e.FullPath, dest, UIOption.AllDialogs, UICancelOption.ThrowException);
            else
                FileSystem.MoveFile(e.FullPath, dest, UIOption.AllDialogs, UICancelOption.ThrowException);
        }
    }

    /// <param name="recycle">true: ごみ箱へ / false: 完全削除</param>
    public static void Delete(IEnumerable<FileEntry> items, bool recycle)
    {
        var opt = recycle ? RecycleOption.SendToRecycleBin : RecycleOption.DeletePermanently;
        foreach (var e in items)
        {
            if (e.IsDirectory)
                FileSystem.DeleteDirectory(e.FullPath, UIOption.OnlyErrorDialogs, opt, UICancelOption.ThrowException);
            else
                FileSystem.DeleteFile(e.FullPath, UIOption.OnlyErrorDialogs, opt, UICancelOption.ThrowException);
        }
    }

    public static void Rename(FileEntry e, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName == e.Name) return;
        if (e.IsDirectory) FileSystem.RenameDirectory(e.FullPath, newName);
        else FileSystem.RenameFile(e.FullPath, newName);
    }

    public static void SetAttributes(IEnumerable<FileEntry> items, FileAttributes set, FileAttributes clear)
    {
        foreach (var e in items)
        {
            var a = File.GetAttributes(e.FullPath);
            a = (a | set) & ~clear;
            File.SetAttributes(e.FullPath, a);
            e.Refresh();
        }
    }

    /// <summary>R: 実行（関連付けで開く）</summary>
    public static void ShellExecute(string path, string? workDir = null)
    {
        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            WorkingDirectory = workDir ?? Path.GetDirectoryName(path) ?? "",
        });
    }

    /// <summary>E: 編集（設定されたエディタで開く）</summary>
    public static void OpenInEditor(string editorExe, string path)
    {
        if (string.IsNullOrWhiteSpace(editorExe)) editorExe = "notepad.exe";
        // 「起動中」カーソル（●のリング）が残らないように FORCEOFFFEEDBACK で起動
        ProcessLauncher.Start(editorExe, $"\"{path}\"", Path.GetDirectoryName(path));
    }

    /// <summary>行番号付きでエディタを開く。lineArgs（$F / $L）が空なら通常どおり開く。</summary>
    public static void OpenInEditorAtLine(string editorExe, string lineArgs, string path, int line)
    {
        if (string.IsNullOrWhiteSpace(lineArgs) || line <= 0) { OpenInEditor(editorExe, path); return; }
        if (string.IsNullOrWhiteSpace(editorExe)) editorExe = "notepad.exe";
        var args = lineArgs.Replace("$F", path).Replace("$L", line.ToString());
        ProcessLauncher.Start(editorExe, args, Path.GetDirectoryName(path));
    }

    /// <summary>X: カレントでターミナルを開く</summary>
    public static void OpenTerminal(string dir)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{dir}\"") { UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = true, WorkingDirectory = dir });
        }
    }

    /// <summary>B: カレントディレクトリで PowerShell を開く（pwsh があればそれを、無ければ Windows PowerShell）。</summary>
    public static void OpenPowerShell(string dir)
    {
        foreach (var exe in new[] { "pwsh.exe", "powershell.exe" })
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe, "-NoLogo") { UseShellExecute = true, WorkingDirectory = dir });
                return;
            }
            catch { /* 次の候補へ */ }
        }
        throw new InvalidOperationException("PowerShell が見つかりません。");
    }

    public static void OpenInExplorer(string path)
        => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    // ---- クリップボード（Ctrl+C / Ctrl+X / Ctrl+V: エクスプローラーと相互運用） ----

    public static void CopyToClipboard(IEnumerable<FileEntry> items, bool cut)
    {
        var paths = new System.Collections.Specialized.StringCollection();
        foreach (var i in items) paths.Add(i.FullPath);
        var data = new System.Windows.DataObject();
        data.SetFileDropList(paths);
        // Preferred DropEffect（DROPEFFECT: 1=Copy 2=Move 4=Link）。エクスプローラーと同じく コピー=5（Copy|Link）、切り取り=2（Move）
        var effect = new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5));
        data.SetData("Preferred DropEffect", effect);
        // WinFM2008 と同じく、テキストとしても入れておく（秀丸やメモ帳に貼るとファイル名の一覧になる。1 行 1 件）
        var names = string.Join(Environment.NewLine, items.Select(i => i.Name));
        if (names.Length > 0) data.SetText(names, System.Windows.TextDataFormat.UnicodeText);
        System.Windows.Clipboard.SetDataObject(data, true);
    }

    /// <summary>クリップボードのファイルを destDir へ貼り付け。切り取りなら移動。戻り値は処理件数。</summary>
    /// <summary>
    /// 同じフォルダへ貼り付けるときの「名前の変更」。新しい名前を返す。null ならその件をスキップ。
    /// 中止したいときは OperationCanceledException を投げる。
    /// </summary>
    public delegate string? AskRename(FileEntry item, bool move);

    public static int PasteFromClipboard(string destDir, AskRename? askRename = null)
    {
        var data = System.Windows.Clipboard.GetDataObject();
        if (data is null) return 0;
        bool move = IsPreferredMove(data);
        var paths = data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? (string[]?)data.GetData(System.Windows.DataFormats.FileDrop) ?? Array.Empty<string>()
            : Array.Empty<string>();
        var items = FromPaths(paths);
        if (items.Count == 0)
        {
            // ローカルに実体が無いファイル（リモートデスクトップのサーバ側でコピーしたもの、Outlook の添付、ZIP 内など）は
            // FileDrop が無い（あってもパスが存在しない）。シェルの貼り付けに任せる。
            if (ShellPaste.HasVirtualFiles(data))
            {
                int n = ShellPaste.CountVirtualFiles(data);
                var eff = ShellPaste.PasteToFolder(destDir, move);
                return eff == 0 ? 0 : Math.Max(n, 1);
            }
            return 0;
        }
        // 同じフォルダへの貼り付け: 1 件ずつ名前を聞く（WinFM2008 と同じ）
        Dictionary<string, string>? renames = null;
        if (askRename is not null)
        {
            var kept = new List<FileEntry>();
            foreach (var it in items)
            {
                var parent = Path.GetDirectoryName(it.FullPath.TrimEnd('\\'))?.TrimEnd('\\') ?? "";
                if (!string.Equals(parent, destDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) { kept.Add(it); continue; }
                var newName = askRename(it, move);   // キャンセルは OperationCanceledException
                if (newName is null) continue;
                (renames ??= new(StringComparer.OrdinalIgnoreCase))[it.FullPath] = newName;
                kept.Add(it);
            }
            items = kept;
            if (items.Count == 0) return 0;
        }
        if (move) Move(items, destDir, renames); else Copy(items, destDir, renames);
        if (move) System.Windows.Clipboard.Clear();
        return items.Count;
    }

    /// <summary>クリップボードの Preferred DropEffect が「移動」（切り取り）か。DROPEFFECT: 1=Copy 2=Move 4=Link（エクスプローラーはコピー=5、切り取り=2）。</summary>
    private static bool IsPreferredMove(System.Windows.IDataObject data)
    {
        if (!data.GetDataPresent("Preferred DropEffect") || data.GetData("Preferred DropEffect") is not Stream ms) return false;
        var buf = new byte[4]; ms.Position = 0; _ = ms.Read(buf, 0, 4);
        int eff = BitConverter.ToInt32(buf, 0);
        return (eff & 2) != 0 && (eff & 1) == 0;
    }

    /// <summary>
    /// その場所にごみ箱があるか。UNC パスやネットワークドライブにはごみ箱が無く、「ごみ箱へ」で削除しても完全削除になる（エクスプローラーも同じ）。
    /// </summary>
    public static bool HasRecycleBin(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return false;
            return new DriveInfo(root).DriveType != DriveType.Network;
        }
        catch { return false; }
    }

    // ---- シェル機能 ----

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHObjectProperties(IntPtr hwnd, uint shopObjectType, string pszObjectName, string? pszPropertyPage);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    /// <summary>Shift+P: エクスプローラーの「プロパティ」ダイアログ。</summary>
    public static void ShowProperties(string path)
    {
        const uint SHOP_FILEPATH = 0x2;
        SHObjectProperties(IntPtr.Zero, SHOP_FILEPATH, path, null);
    }

    /// <summary>Shift+G: ごみ箱を空にする（確認ダイアログあり）。</summary>
    public static void EmptyRecycleBin() => SHEmptyRecycleBin(IntPtr.Zero, null, 0);

    /// <summary>Ctrl+G: ごみ箱を開く。</summary>
    public static void OpenRecycleBin()
        => Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });

    /// <summary>Ctrl+N: 空のファイルを作る。</summary>
    public static void CreateEmptyFile(string path)
    {
        if (File.Exists(path)) throw new IOException($"既に存在します: {Path.GetFileName(path)}");
        using var _ = File.Create(path);
    }

    /// <summary>Ctrl+T: タイムスタンプ（更新日時）の変更。</summary>
    public static void SetTimestamp(IEnumerable<FileEntry> items, DateTime when)
    {
        foreach (var e in items)
        {
            if (e.IsDirectory) Directory.SetLastWriteTime(e.FullPath, when);
            else File.SetLastWriteTime(e.FullPath, when);
        }
    }

    /// <summary>Ctrl+U: ファイル名の大文字・小文字変換。mode: upper / lower / ext-lower / capital</summary>
    public static int ConvertCase(IEnumerable<FileEntry> items, string mode)
    {
        int n = 0;
        foreach (var e in items)
        {
            var name = e.Name;
            var stem = e.IsDirectory ? name : Path.GetFileNameWithoutExtension(name);
            var ext = e.IsDirectory ? "" : Path.GetExtension(name);
            var newName = mode switch
            {
                "upper" => name.ToUpperInvariant(),
                "lower" => name.ToLowerInvariant(),
                "ext-lower" => stem + ext.ToLowerInvariant(),
                "capital" => (stem.Length > 0 ? char.ToUpperInvariant(stem[0]) + stem[1..].ToLowerInvariant() : stem) + ext.ToLowerInvariant(),
                _ => name,
            };
            if (newName == name) continue;
            // 大文字小文字だけの違いは Windows では同名扱いなので、一旦別名を経由する
            var dir = Path.GetDirectoryName(e.FullPath)!;
            var tmp = Path.Combine(dir, name + ".~case~");
            if (e.IsDirectory) { Directory.Move(e.FullPath, tmp); Directory.Move(tmp, Path.Combine(dir, newName)); }
            else { File.Move(e.FullPath, tmp); File.Move(tmp, Path.Combine(dir, newName)); }
            n++;
        }
        return n;
    }

    /// <summary>X: コマンドの実行。$P=カレントパス $C=カレントファイル $X=拡張子なし $T=タグファイル列 $Q="</summary>
    public static void ExecCommand(string commandLine, string dir, FileEntry? current, IEnumerable<FileEntry> tagged)
    {
        string q(string s) => s.Contains(' ') ? $"\"{s}\"" : s;
        var cmd = commandLine
            .Replace("$Q", "\"")
            .Replace("$P", dir)
            .Replace("$C", current?.Name ?? "")
            .Replace("$X", current is null ? "" : Path.GetFileNameWithoutExtension(current.Name))
            .Replace("$T", string.Join(" ", tagged.Select(t => q(t.Name))));
        Process.Start(new ProcessStartInfo("cmd.exe", "/k " + cmd) { UseShellExecute = true, WorkingDirectory = dir });
    }

    public static long DirectorySize(string dir)
    {
        long total = 0;
        try
        {
            var opt = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = 0 };
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", opt)) total += f.Length;
        }
        catch { }
        return total;
    }
}
