using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FDLike2026.Services;

/// <summary>
/// エクスプローラーと同じ Windows のシェルコンテキストメニュー（IContextMenu）を表示する。
/// 7-Zip / WinMerge / 送る / プロパティなど、インストール済みソフトが登録した項目がそのまま使える。
/// 外部 DLL 不要（shell32 / user32 の COM 相互運用のみ）。
/// </summary>
public static class ShellContextMenu
{
    /// <summary>
    /// paths（同じフォルダ内のファイル / フォルダ）のシェルメニューを画面座標 (x, y) に出し、選ばれた項目を実行する。
    /// paths が空なら folder の背景メニュー（新規作成・貼り付けなど）を出す。
    /// </summary>
    public static void Show(Window owner, string folder, IReadOnlyList<string> paths, int x, int y)
    {
        var hwnd = new WindowInteropHelper(owner).Handle;
        IntPtr hMenu = IntPtr.Zero;
        object? cmObj = null;
        var pidls = new List<IntPtr>();
        object? shellFolder = null;
        HwndSource? source = HwndSource.FromHwnd(hwnd);
        HwndSourceHook? hook = null;
        try
        {
            IntPtr pcm;
            var iidCM = typeof(IContextMenu).GUID;
            if (paths.Count > 0)
            {
                // 親フォルダの IShellFolder と、各項目の相対 PIDL
                IntPtr parentPtr = IntPtr.Zero;
                var children = new IntPtr[paths.Count];
                for (int i = 0; i < paths.Count; i++)
                {
                    Check(SHParseDisplayName(paths[i], IntPtr.Zero, out var full, 0, out _), paths[i]);
                    pidls.Add(full);
                    var iidSF = typeof(IShellFolder).GUID;
                    Check(SHBindToParent(full, ref iidSF, out var sf, out var child), paths[i]);
                    if (parentPtr == IntPtr.Zero) parentPtr = sf; else Marshal.Release(sf);
                    children[i] = child;
                }
                shellFolder = Marshal.GetTypedObjectForIUnknown(parentPtr, typeof(IShellFolder));
                Marshal.Release(parentPtr);
                Check(((IShellFolder)shellFolder).GetUIObjectOf(hwnd, (uint)children.Length, children, ref iidCM, IntPtr.Zero, out pcm), "GetUIObjectOf");
            }
            else
            {
                var iidSF = typeof(IShellFolder).GUID;
                Check(SHParseDisplayName(folder, IntPtr.Zero, out var fpidl, 0, out _), folder);
                pidls.Add(fpidl);
                Check(SHBindToObject(IntPtr.Zero, fpidl, IntPtr.Zero, ref iidSF, out var sfPtr), folder);
                shellFolder = Marshal.GetTypedObjectForIUnknown(sfPtr, typeof(IShellFolder));
                Marshal.Release(sfPtr);
                Check(((IShellFolder)shellFolder).CreateViewObject(hwnd, ref iidCM, out pcm), "CreateViewObject");
            }
            cmObj = Marshal.GetTypedObjectForIUnknown(pcm, typeof(IContextMenu));
            Marshal.Release(pcm);
            var cm = (IContextMenu)cmObj;
            var cm2 = cmObj as IContextMenu2;
            var cm3 = cmObj as IContextMenu3;

            hMenu = CreatePopupMenu();
            uint flags = CMF_NORMAL | CMF_EXPLORE;
            if ((GetKeyState(VK_SHIFT) & 0x8000) != 0) flags |= CMF_EXTENDEDVERBS;   // Shift で拡張項目
            Check(cm.QueryContextMenu(hMenu, 0, ID_FIRST, ID_LAST, flags), "QueryContextMenu");

            // 「送る」「7-Zip」などのサブメニューは、所有者ウィンドウに来るメニューメッセージを IContextMenu2/3 に渡さないと中身が作られない
            if (source is not null && (cm2 is not null || cm3 is not null))
            {
                hook = (IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (msg is WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR)
                    {
                        if (cm3 is not null)
                        {
                            if (cm3.HandleMenuMsg2((uint)msg, wParam, lParam, out var res) == 0) { handled = true; return res; }
                        }
                        else if (cm2!.HandleMenuMsg((uint)msg, wParam, lParam) == 0) { handled = true; return IntPtr.Zero; }
                    }
                    return IntPtr.Zero;
                };
                source.AddHook(hook);
            }

            uint id = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_LEFTALIGN | TPM_TOPALIGN | TPM_LEFTBUTTON | TPM_RIGHTBUTTON, x, y, hwnd, IntPtr.Zero);
            if (id == 0) return;   // キャンセル

            var dirPtr = Marshal.StringToHGlobalUni(folder);
            try
            {
                var ici = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                    hwnd = hwnd,
                    lpVerb = (IntPtr)(id - ID_FIRST),
                    lpVerbW = (IntPtr)(id - ID_FIRST),
                    lpDirectory = IntPtr.Zero,
                    lpDirectoryW = dirPtr,
                    nShow = SW_SHOWNORMAL,
                    ptInvoke = new POINT { x = x, y = y },
                };
                Check(cm.InvokeCommand(ref ici), "InvokeCommand");
            }
            finally { Marshal.FreeHGlobal(dirPtr); }
        }
        finally
        {
            if (hook is not null) source!.RemoveHook(hook);
            if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            if (cmObj is not null) Marshal.FinalReleaseComObject(cmObj);
            if (shellFolder is not null) Marshal.FinalReleaseComObject(shellFolder);
            foreach (var p in pidls) ILFree(p);
        }
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0) throw new System.ComponentModel.Win32Exception(hr, $"{what}: {Marshal.GetExceptionForHR(hr)?.Message}");
    }

    // ---- 定数 ----
    private const uint ID_FIRST = 1, ID_LAST = 0x7FFF;
    private const uint CMF_NORMAL = 0x0, CMF_EXPLORE = 0x4, CMF_EXTENDEDVERBS = 0x100;
    private const uint TPM_LEFTBUTTON = 0x0, TPM_RIGHTBUTTON = 0x2, TPM_LEFTALIGN = 0x0, TPM_TOPALIGN = 0x0, TPM_RETURNCMD = 0x100;
    private const int CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_PTINVOKE = 0x20000000;
    private const int SW_SHOWNORMAL = 1;
    private const int WM_INITMENUPOPUP = 0x117, WM_DRAWITEM = 0x2B, WM_MEASUREITEM = 0x2C, WM_MENUCHAR = 0x120;
    private const int VK_SHIFT = 0x10;

    // ---- 構造体 ----
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize; public int fMask; public IntPtr hwnd; public IntPtr lpVerb; public IntPtr lpParameters; public IntPtr lpDirectory;
        public int nShow; public int dwHotKey; public IntPtr hIcon; public IntPtr lpTitle;
        public IntPtr lpVerbW; public IntPtr lpParametersW; public IntPtr lpDirectoryW; public IntPtr lpTitleW; public POINT ptInvoke;
    }

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

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    // ---- P/Invoke ----
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);
    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);
    [DllImport("shell32.dll")]
    private static extern int SHBindToObject(IntPtr psf, IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);
    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);
    [DllImport("user32.dll")]
    private static extern short GetKeyState(int vKey);
}
