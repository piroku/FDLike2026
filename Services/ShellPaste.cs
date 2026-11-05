using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace FDLike2026.Services;

/// <summary>
/// クリップボードに「実体がローカルに無いファイル」（FileGroupDescriptorW + FileContents）が入っているときの貼り付け。
/// リモートデスクトップ（mstsc）でサーバ側からコピーしたファイル、Outlook の添付、ZIP フォルダ内のファイルなどが該当する。
/// 自前で中身を読まず、貼り付け先フォルダのシェルの IDropTarget にクリップボードの IDataObject をそのまま渡す
/// （エクスプローラーの「貼り付け」と同じ経路。進捗ダイアログもシェルが出す）。
/// </summary>
public static class ShellPaste
{
    public const string FileGroupDescriptorW = "FileGroupDescriptorW";
    public const string FileGroupDescriptorA = "FileGroupDescriptor";

    /// <summary>クリップボードに仮想ファイル（FileGroupDescriptor）が入っているか。</summary>
    public static bool HasVirtualFiles(System.Windows.IDataObject data)
        => data.GetDataPresent(FileGroupDescriptorW) || data.GetDataPresent(FileGroupDescriptorA);

    /// <summary>FileGroupDescriptor の件数（先頭 4 バイトの cItems）。取れなければ -1。</summary>
    public static int CountVirtualFiles(System.Windows.IDataObject data)
    {
        try
        {
            var fmt = data.GetDataPresent(FileGroupDescriptorW) ? FileGroupDescriptorW : FileGroupDescriptorA;
            if (data.GetData(fmt) is Stream s && s.Length >= 4)
            {
                var buf = new byte[4]; s.Position = 0; _ = s.Read(buf, 0, 4);
                return BitConverter.ToInt32(buf, 0);
            }
        }
        catch { }
        return -1;
    }

    /// <summary>
    /// クリップボードの内容を destDir のシェル IDropTarget へ Drop する。
    /// 戻り値: シェルが受け付けた効果（DROPEFFECT）。0 なら貼り付け先がこのデータを受け付けなかった。
    /// </summary>
    public static uint PasteToFolder(string destDir, bool move)
    {
        Directory.CreateDirectory(destDir);
        var hwnd = System.Windows.Application.Current?.MainWindow is { } mw ? new WindowInteropHelper(mw).Handle : IntPtr.Zero;

        Check(OleGetClipboard(out var dataObj), "OleGetClipboard");
        IntPtr pidl = IntPtr.Zero;
        object? sfObj = null, dtObj = null;
        try
        {
            var iidSF = typeof(IShellFolder).GUID;
            var iidDT = typeof(IDropTarget).GUID;
            Check(SHParseDisplayName(destDir, IntPtr.Zero, out pidl, 0, out _), destDir);
            Check(SHBindToObject(IntPtr.Zero, pidl, IntPtr.Zero, ref iidSF, out var sfPtr), destDir);
            sfObj = Marshal.GetTypedObjectForIUnknown(sfPtr, typeof(IShellFolder));
            Marshal.Release(sfPtr);
            Check(((IShellFolder)sfObj).CreateViewObject(hwnd, ref iidDT, out var dtPtr), "CreateViewObject(IDropTarget)");
            dtObj = Marshal.GetTypedObjectForIUnknown(dtPtr, typeof(IDropTarget));
            Marshal.Release(dtPtr);
            var dt = (IDropTarget)dtObj;

            // エクスプローラーの貼り付けと同じ: 左ボタン + Ctrl（コピー）/ Shift（移動）で効果を固定する
            uint keyState = MK_LBUTTON | (move ? MK_SHIFT : MK_CONTROL);
            uint allowed = move ? DROPEFFECT_MOVE : DROPEFFECT_COPY;
            var pt = new POINTL { x = 0, y = 0 };

            uint effect = allowed;
            Check(dt.DragEnter(dataObj, keyState, pt, ref effect), "DragEnter");
            if (effect == DROPEFFECT_NONE)
            {
                dt.DragLeave();
                return DROPEFFECT_NONE;
            }
            effect = allowed;
            Check(dt.Drop(dataObj, keyState, pt, ref effect), "Drop");
            return effect;
        }
        finally
        {
            if (dtObj is not null) Marshal.ReleaseComObject(dtObj);
            if (sfObj is not null) Marshal.ReleaseComObject(sfObj);
            if (pidl != IntPtr.Zero) ILFree(pidl);
            Marshal.ReleaseComObject(dataObj);
        }
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0) throw new COMException($"{what} に失敗しました", hr);
    }

    // ---- 定数 ----
    private const uint MK_LBUTTON = 0x0001, MK_SHIFT = 0x0004, MK_CONTROL = 0x0008;
    private const uint DROPEFFECT_NONE = 0, DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL { public int x; public int y; }

    // ---- COM インターフェイス ----
    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int grfFlags, out IntPtr ppenumIDList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTarget
    {
        [PreserveSig] int DragEnter(ComIDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
        [PreserveSig] int DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop(ComIDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
    }

    // ---- P/Invoke ----
    [DllImport("ole32.dll")]
    private static extern int OleGetClipboard(out ComIDataObject ppDataObj);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);
    [DllImport("shell32.dll")]
    private static extern int SHBindToObject(IntPtr psf, IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);
}
