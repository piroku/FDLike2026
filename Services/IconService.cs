using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FDLike2026.Services;

/// <summary>
/// エクスプローラーと同じシェルアイコン（小）を取得してキャッシュする。
/// 一般のファイルは拡張子単位、exe / lnk / ico とフォルダ・ドライブはパス単位でキャッシュ。
/// </summary>
public static class IconService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPath;
    }

    [DllImport("shell32.dll", SetLastError = false)]
    private static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);

    private const uint SIID_DRIVENET = 9;

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _folder;
    private static ImageSource? _netDrive;

    /// <summary>Ctrl+I でのアイコン表示 ON/OFF（表示側は DynamicResource で切り替え、ここはキャッシュだけ）。</summary>
    public static void ClearCache() { Cache.Clear(); _folder = null; _netDrive = null; }

    /// <summary>シェルの標準アイコン（ディスクにアクセスしない）。</summary>
    private static ImageSource? LoadStock(uint siid)
    {
        var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
        if (SHGetStockIconInfo(siid, SHGFI_ICON | SHGFI_SMALLICON, ref info) != 0 || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { DestroyIcon(info.hIcon); }
    }

    public static ImageSource? ForFile(string fullPath, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                // 通常フォルダは共通アイコン。ドライブルートや特殊フォルダ（desktop.ini あり）は個別に。
                var isRoot = fullPath.Length <= 3;
                // ネットワークドライブのルートに SHGetFileInfo で実アクセスすると相手先停止中に固まるので、
                // ネットワークドライブは標準の「ネットワーク ドライブ」アイコンで済ませる
                if (isRoot && PathProbe.IsNetworkPath(fullPath))
                    return _netDrive ??= LoadStock(SIID_DRIVENET);
                if (!isRoot && PathProbe.IsUnreachable(fullPath))
                    return _folder ??= Load("folder", FILE_ATTRIBUTE_DIRECTORY, useAttrs: true);
                if (!isRoot && !File.Exists(Path.Combine(fullPath, "desktop.ini")))
                    return _folder ??= Load("folder", FILE_ATTRIBUTE_DIRECTORY, useAttrs: true);
                return Cache.GetOrAdd(fullPath, p => Load(p, FILE_ATTRIBUTE_DIRECTORY, useAttrs: false));
            }
            var ext = Path.GetExtension(fullPath);
            bool perFile = ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) || ext.Equals(".url", StringComparison.OrdinalIgnoreCase);
            if (perFile)
            {
                if (Cache.Count > 2000) Cache.Clear();
                return Cache.GetOrAdd(fullPath, p => Load(p, FILE_ATTRIBUTE_NORMAL, useAttrs: false));
            }
            return Cache.GetOrAdd(ext.Length == 0 ? "(none)" : ext, _ => Load("dummy" + ext, FILE_ATTRIBUTE_NORMAL, useAttrs: true));
        }
        catch { return null; }
    }

    private static ImageSource? Load(string path, uint attrs, bool useAttrs)
    {
        var info = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_SMALLICON | (useAttrs ? SHGFI_USEFILEATTRIBUTES : 0);
        var r = SHGetFileInfo(path, attrs, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (r == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { DestroyIcon(info.hIcon); }
    }
}
