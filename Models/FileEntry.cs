using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace FDLike2026.Models;

/// <summary>ファイル一覧の 1 行。WinFM の「名前 / 拡張子 / サイズ / 日付 / 時刻 / 属性」に相当。</summary>
public sealed class FileEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string FullPath { get; }
    public string Name { get; }
    public bool IsDirectory { get; }
    public bool IsParent { get; }           // ".." 行
    public long Size { get; }
    public DateTime LastWrite { get; }
    public DateTime Created { get; }
    public DateTime Accessed { get; }
    public FileAttributes Attributes { get; private set; }

    private bool _tagged;
    public bool Tagged
    {
        get => _tagged;
        set { if (_tagged != value) { _tagged = value; OnChanged(); } }
    }

    private bool _isCursor;
    public bool IsCursor
    {
        get => _isCursor;
        set { if (_isCursor != value) { _isCursor = value; OnChanged(); } }
    }

    private System.Windows.GridLength _nameColWidth = new(1, System.Windows.GridUnitType.Star);
    /// <summary>名前列の幅（所属するペインが一覧ごとに決めて配る）。</summary>
    public System.Windows.GridLength NameColWidth
    {
        get => _nameColWidth;
        set { if (_nameColWidth != value) { _nameColWidth = value; OnChanged(); } }
    }

    private string _gitText = "", _gitKey = "";
    /// <summary>git の状態（M / U / A / D / R / ! / ●）。管理外は空。</summary>
    public string GitText { get => _gitText; private set { if (_gitText != value) { _gitText = value; OnChanged(); } } }
    /// <summary>色分け用キー（Modified / Untracked / Added / Deleted / Ignored）。</summary>
    public string GitKey { get => _gitKey; private set { if (_gitKey != value) { _gitKey = value; OnChanged(); } } }
    private System.Windows.GridLength _extColWidth = new(0);
    /// <summary>拡張子列の幅（ペインが配る。文字数自動なら一覧内の最長拡張子に合わせる）。</summary>
    public System.Windows.GridLength ExtColWidth
    {
        get => _extColWidth;
        set { if (_extColWidth != value) { _extColWidth = value; OnChanged(); } }
    }

    public void SetGit(string text, string key) { GitText = text; GitKey = key; }

    /// <summary>表示モードが変わったとき、書式に関わるプロパティを再通知する。</summary>
    public void RefreshDisplay()
    {
        foreach (var n in new[] { nameof(BaseName), nameof(Extension), nameof(SizeText), nameof(DateText), nameof(TimeText), nameof(AttrText) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public FileEntry(FileSystemInfo info)
    {
        FullPath = info.FullName;
        Name = info.Name;
        IsDirectory = info is DirectoryInfo;
        Attributes = info.Attributes;
        LastWrite = info.LastWriteTime;
        Created = info.CreationTime;
        Accessed = info.LastAccessTime;
        Size = info is FileInfo fi ? fi.Length : 0;
    }

    private FileEntry(string fullPath, string name, bool isParent)
    {
        FullPath = fullPath;
        Name = name;
        IsDirectory = true;
        IsParent = isParent;
        Attributes = FileAttributes.Directory;
        LastWrite = Directory.Exists(fullPath) ? Directory.GetLastWriteTime(fullPath) : DateTime.MinValue;
        Created = LastWrite; Accessed = LastWrite;
    }

    public static FileEntry CreateParent(string parentPath) => new(parentPath, "..", true);

    public bool IsHidden  => Attributes.HasFlag(FileAttributes.Hidden);
    public bool IsSystem  => Attributes.HasFlag(FileAttributes.System);
    public bool IsReadOnly => Attributes.HasFlag(FileAttributes.ReadOnly);
    public bool IsArchive => Attributes.HasFlag(FileAttributes.Archive);
    public bool IsReparse => Attributes.HasFlag(FileAttributes.ReparsePoint);
    public bool IsEncrypted => Attributes.HasFlag(FileAttributes.Encrypted);
    public bool IsCompressed => Attributes.HasFlag(FileAttributes.Compressed);

    private static Services.ListMode Mode => Services.ListModeService.Current;

    /// <summary>表示モードに応じた名前列。拡張子を分離しないときは拡張子込み。8.3 形式も可。</summary>
    public string BaseName
    {
        get
        {
            bool split = Mode.SplitExt && !(IsDirectory && Mode.DirNoSplit) && !IsDirectory;
            if (Mode.FileNameMode == "dos") return ShortName(split);
            if (!split) return Name;
            return Path.GetFileNameWithoutExtension(Name);
        }
    }

    /// <summary>拡張子列（ドット付き）。分離しない設定・ディレクトリは空。</summary>
    public string Extension
    {
        get
        {
            if (IsDirectory || !Mode.SplitExt) return "";
            var ext = Path.GetExtension(Name);
            if (Mode.FileNameMode == "dos" && ext.Length > 4) ext = ext[..4];
            return ext;
        }
    }

    /// <summary>MS-DOS 8.3 風の短縮表記（実際の短い名前ではなく見た目だけ）。</summary>
    private string ShortName(bool split)
    {
        var stem = IsDirectory ? Name : Path.GetFileNameWithoutExtension(Name);
        if (stem.Length > 8) stem = stem[..6] + "~1";
        if (split || IsDirectory) return stem.ToUpperInvariant();
        var ext = Path.GetExtension(Name);
        if (ext.Length > 4) ext = ext[..4];
        return (stem + ext).ToUpperInvariant();
    }

    /// <summary>サイズ列。ディレクトリはフォルダ表記、簡略表示なら MB/GB。</summary>
    public string SizeText
    {
        get
        {
            if (IsDirectory) return IsParent ? "" : Services.ListModeService.DirLabel;
            if (Mode.SizeShort)
            {
                if (Size >= 10L * 1024 * 1024 * 1024) return $"{Size / (1024.0 * 1024 * 1024):#,0}GB";
                if (Size >= 10L * 1024 * 1024) return $"{Size / (1024.0 * 1024):#,0}MB";
            }
            return Size.ToString("#,0");
        }
    }

    /// <summary>日付の種類（更新 / 作成 / アクセス）に応じた日時。</summary>
    public DateTime Stamp => Mode.DateKind switch { "create" => Created, "access" => Accessed, _ => LastWrite };

    public string DateText => Stamp == DateTime.MinValue ? "" : new string(' ', Math.Max(0, Mode.DatePad - 1)) + Stamp.ToString(Mode.Year4 ? "yyyy/MM/dd" : "yy/MM/dd");
    public string TimeText => Stamp == DateTime.MinValue ? "" : Stamp.ToString(Mode.ShowSeconds ? "HH:mm:ss" : "HH:mm");
    public string DateFullText => LastWrite == DateTime.MinValue ? "" : LastWrite.ToString("yyyy/MM/dd");
    public string TimeFullText => LastWrite == DateTime.MinValue ? "" : LastWrite.ToString("HH:mm:ss");

    /// <summary>属性列 "RHSAD" の形式。付いていない属性は空白。</summary>
    public string AttrText =>
        (IsReadOnly ? "R" : " ") + (IsHidden ? "H" : " ") + (IsSystem ? "S" : " ") +
        (IsArchive ? "A" : " ") + (IsDirectory ? "D" : " ");

    /// <summary>シェルアイコン（小）。表示時に初めて取得し、以後キャッシュ。</summary>
    private System.Windows.Media.ImageSource? _icon;
    private bool _iconLoaded;
    public System.Windows.Media.ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded) { _icon = IsParent ? null : Services.IconService.ForFile(FullPath, IsDirectory); _iconLoaded = true; }
            return _icon;
        }
    }

    public void Refresh()
    {
        try
        {
            Attributes = File.GetAttributes(FullPath);
            OnChanged(nameof(Attributes));
            OnChanged(nameof(AttrText));
        }
        catch { /* 消えた場合など */ }
    }

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
