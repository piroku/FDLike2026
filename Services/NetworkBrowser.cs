using System.Runtime.InteropServices;

namespace FDLike2026.Services;

/// <summary>
/// ツリーの「ネットワーク」用: ネットワーク上のコンピューターと、その共有フォルダの一覧。
/// どれも相手先を待つことがあるので、UI スレッドからは呼ばず <see cref="RunAsync{T}"/> で裏から呼ぶ。
/// </summary>
public static class NetworkBrowser
{
    /// <summary>バックグラウンドの STA スレッドで実行する（シェルの COM を使うため）。</summary>
    public static Task<T> RunAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>();
        var th = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true, Name = "NetworkBrowser" };
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        return tcs.Task;
    }

    // ---- コンピューターの一覧 ------------------------------------------------

    /// <summary>
    /// ネットワーク上のコンピューター名（"\\" なし）。エクスプローラーの「ネットワーク」に出るもの
    /// ＋ 従来の Windows ネットワークの列挙。見つからなければ空。
    /// </summary>
    public static List<string> EnumHosts()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in ShellHosts(null)) set.Add(h);
        foreach (var h in WNetHosts()) set.Add(h);
        return set.ToList();
    }

    /// <summary>エクスプローラーの「ネットワーク」に出るコンピューター。</summary>
    /// <remarks>全部そろうまで数十秒かかることがあるので、見つかるたびに <paramref name="onFound"/> を呼ぶ（呼び出し元のスレッドで）。</remarks>
    public static List<string> ShellHosts(Action<string>? onFound)
    {
        var list = new List<string>();
        try
        {
            if (ShellEnumNetwork(h => { list.Add(h); onFound?.Invoke(h); })) return list;
        }
        catch { }
        // 上の方法が使えなかったときの予備（結果は最後にまとめて）
        foreach (var h in ShellNetworkItems())
            if (!list.Contains(h, StringComparer.OrdinalIgnoreCase)) { list.Add(h); onFound?.Invoke(h); }
        return list;
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr pchEaten, out IntPtr ppidl, IntPtr pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int grfFlags, out IEnumIDList? ppenum);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, IntPtr apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, Guid("000214F2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumIDList
    {
        [PreserveSig] int Next(uint celt, out IntPtr rgelt, out uint pceltFetched);
        [PreserveSig] int Skip(uint celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumIDList? ppenum);
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetDesktopFolder([MarshalAs(UnmanagedType.Interface)] out IShellFolder ppshf);

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderIDList(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppidl);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrRetToBufW(IntPtr pstr, IntPtr pidl, System.Text.StringBuilder pszBuf, uint cchBuf);

    private const int SHCONTF_FOLDERS = 0x20, SHCONTF_NONFOLDERS = 0x40;
    private const uint SHGDN_FORPARSING = 0x8000;

    /// <summary>「ネットワーク」フォルダを 1 件ずつ列挙する。列挙を始められたら true。</summary>
    private static bool ShellEnumNetwork(Action<string> onFound)
    {
        var networkFolder = new Guid("D20BEEC4-5CA8-4905-AE3B-BF251EA09B53");   // FOLDERID_NetworkFolder
        var iidShellFolder = new Guid("000214E6-0000-0000-C000-000000000046");
        IntPtr pidlNet = IntPtr.Zero, strret = IntPtr.Zero;
        IShellFolder? desktop = null, net = null;
        IEnumIDList? en = null;
        try
        {
            if (SHGetKnownFolderIDList(ref networkFolder, 0, IntPtr.Zero, out pidlNet) != 0) return false;
            if (SHGetDesktopFolder(out desktop) != 0) return false;
            if (desktop.BindToObject(pidlNet, IntPtr.Zero, ref iidShellFolder, out var o) != 0 || o is not IShellFolder nf) return false;
            net = nf;
            if (net.EnumObjects(IntPtr.Zero, SHCONTF_FOLDERS | SHCONTF_NONFOLDERS, out en) != 0 || en is null) return false;
            strret = Marshal.AllocCoTaskMem(600);
            var sb = new System.Text.StringBuilder(1024);
            while (en.Next(1, out var pidl, out var fetched) == 0 && fetched == 1)
            {
                try
                {
                    for (int i = 0; i < 600; i += 8) Marshal.WriteInt64(strret, i, 0);
                    if (net.GetDisplayNameOf(pidl, SHGDN_FORPARSING, strret) == 0 &&
                        StrRetToBufW(strret, pidl, sb.Clear(), (uint)sb.Capacity) == 0 &&
                        HostFromUnc(sb.ToString()) is { } h)
                        onFound(h);
                }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
            return true;
        }
        finally
        {
            if (strret != IntPtr.Zero) Marshal.FreeCoTaskMem(strret);
            if (pidlNet != IntPtr.Zero) Marshal.FreeCoTaskMem(pidlNet);
            if (en is not null) Marshal.ReleaseComObject(en);
            if (net is not null) Marshal.ReleaseComObject(net);
            if (desktop is not null) Marshal.ReleaseComObject(desktop);
        }
    }

    /// <summary>従来の Windows ネットワークの列挙（ドメイン / ワークグループ配下のサーバー）。環境によっては数十秒かかる。</summary>
    public static List<string> WNetHosts()
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        try { WNetHosts(IntPtr.Zero, 0, set); } catch { }
        return set.ToList();
    }

    /// <summary>エクスプローラーの「ネットワーク」フォルダの中身から、\\HOST の形のものだけを取り出す。</summary>
    private static List<string> ShellNetworkItems()
    {
        var list = new List<string>();
        object? shell = null;
        try
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t is null) return list;
            shell = Activator.CreateInstance(t);
            if (shell is null) return list;
            dynamic sh = shell;
            dynamic? folder = sh.NameSpace(0x12);   // ssfNETWORK
            if (folder is null) return list;
            foreach (dynamic item in folder.Items())
            {
                try
                {
                    string path = item.Path;
                    if (HostFromUnc(path) is { } h) list.Add(h);
                }
                catch { }
            }
        }
        catch { }
        finally
        {
            if (shell is not null) { try { Marshal.FinalReleaseComObject(shell); } catch { } }
        }
        return list;
    }

    /// <summary>"\\HOST" または "\\HOST\..." からホスト名。違う形なら null。</summary>
    public static string? HostFromUnc(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        var rest = path.Substring(2);
        if (rest.StartsWith("?\\", StringComparison.Ordinal) || rest.StartsWith(".\\", StringComparison.Ordinal)) return null;
        int i = rest.IndexOf('\\');
        var host = i < 0 ? rest : rest.Substring(0, i);
        return host.Length == 0 ? null : host;
    }

    // ---- 共有フォルダの一覧 --------------------------------------------------

    /// <summary>ホストの共有フォルダ名（ディスクの共有のみ。C$ などの管理共有は除く）。取得できなければ null。</summary>
    public static List<string>? EnumShares(string host)
    {
        var a = NetApiShares(host);
        if (a is not null && a.Count > 0) return a;
        List<string>? b = null;
        try { b = WNetShares(host); } catch { }
        if (b is not null && b.Count > 0) return b;
        return a ?? b;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHARE_INFO_1
    {
        public IntPtr shi1_netname;
        public uint shi1_type;
        public IntPtr shi1_remark;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareEnum(string servername, int level, out IntPtr bufptr, int prefmaxlen,
        out int entriesread, out int totalentries, ref int resumeHandle);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    private const uint STYPE_MASK = 0xFF;          // 0 = ディスク
    private const uint STYPE_SPECIAL = 0x80000000; // 管理共有（C$ / ADMIN$ / IPC$）

    private static List<string>? NetApiShares(string host)
    {
        var list = new List<string>();
        int resume = 0;
        IntPtr buf = IntPtr.Zero;
        try
        {
            int rc = NetShareEnum(@"\\" + host, 1, out buf, -1, out int read, out _, ref resume);
            if (rc != 0 || buf == IntPtr.Zero) return null;
            int size = Marshal.SizeOf<SHARE_INFO_1>();
            for (int i = 0; i < read; i++)
            {
                var s = Marshal.PtrToStructure<SHARE_INFO_1>(buf + i * size);
                if ((s.shi1_type & STYPE_MASK) != 0 || (s.shi1_type & STYPE_SPECIAL) != 0) continue;
                var name = Marshal.PtrToStringUni(s.shi1_netname);
                if (!string.IsNullOrEmpty(name) && !name.EndsWith('$')) list.Add(name);
            }
        }
        catch { return null; }
        finally { if (buf != IntPtr.Zero) NetApiBufferFree(buf); }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    // ---- WNet（Windows ネットワークの列挙） ----------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public IntPtr lpLocalName;
        public IntPtr lpRemoteName;
        public IntPtr lpComment;
        public IntPtr lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetOpenEnumW(int dwScope, int dwType, int dwUsage, IntPtr lpNetResource, out IntPtr lphEnum);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetEnumResourceW(IntPtr hEnum, ref int lpcCount, IntPtr lpBuffer, ref int lpBufferSize);

    [DllImport("mpr.dll")]
    private static extern int WNetCloseEnum(IntPtr hEnum);

    private const int RESOURCE_GLOBALNET = 2;
    private const int RESOURCETYPE_ANY = 0;
    private const int RESOURCETYPE_DISK = 1;
    private const int RESOURCEDISPLAYTYPE_SERVER = 2;
    private const int RESOURCEDISPLAYTYPE_SHARE = 3;
    private const int RESOURCEUSAGE_CONTAINER = 2;
    private const int MaxHosts = 500;

    /// <summary>コンテナー（null = 最上位）の直下を列挙する。</summary>
    private static List<(NETRESOURCE res, string? remote, string? provider)> WNetChildren(IntPtr container, int type, IntPtr buffer, int bufSize)
    {
        var result = new List<(NETRESOURCE, string?, string?)>();
        if (WNetOpenEnumW(RESOURCE_GLOBALNET, type, 0, container, out var h) != 0) return result;
        try
        {
            while (true)
            {
                int count = -1, size = bufSize;
                int rc = WNetEnumResourceW(h, ref count, buffer, ref size);
                if (rc != 0 || count <= 0) break;   // ERROR_NO_MORE_ITEMS など
                int step = Marshal.SizeOf<NETRESOURCE>();
                for (int i = 0; i < count; i++)
                {
                    var p = buffer + i * step;
                    var r = Marshal.PtrToStructure<NETRESOURCE>(p);
                    result.Add((r, r.lpRemoteName == IntPtr.Zero ? null : Marshal.PtrToStringUni(r.lpRemoteName),
                                   r.lpProvider == IntPtr.Zero ? null : Marshal.PtrToStringUni(r.lpProvider)));
                }
            }
        }
        finally { WNetCloseEnum(h); }
        return result;
    }

    /// <summary>プロバイダー → ドメイン / ワークグループ → サーバー と辿ってサーバー名を集める。</summary>
    private static void WNetHosts(IntPtr container, int depth, ISet<string> found)
    {
        if (depth > 3) return;
        const int size = 32 * 1024;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            foreach (var (res, remote, provider) in WNetChildren(container, RESOURCETYPE_ANY, buf, size))
            {
                if (found.Count >= MaxHosts) return;
                if (res.dwDisplayType == RESOURCEDISPLAYTYPE_SERVER)
                {
                    if (HostFromUnc(remote) is { } h) found.Add(h);
                }
                else if ((res.dwUsage & RESOURCEUSAGE_CONTAINER) != 0)
                {
                    // 子の列挙でバッファを使い回さないよう、この項目を別メモリに写してから潜る
                    var copy = CloneResource(res, remote, provider);
                    try { WNetHosts(copy.ptr, depth + 1, found); }
                    finally { copy.Free(); }
                }
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static List<string>? WNetShares(string host)
    {
        const int size = 32 * 1024;
        var res = new NETRESOURCE { dwScope = RESOURCE_GLOBALNET, dwType = RESOURCETYPE_DISK, dwDisplayType = RESOURCEDISPLAYTYPE_SERVER, dwUsage = RESOURCEUSAGE_CONTAINER };
        var copy = CloneResource(res, @"\\" + host, null);
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (WNetOpenEnumW(RESOURCE_GLOBALNET, RESOURCETYPE_DISK, 0, copy.ptr, out var probe) != 0) return null;
            WNetCloseEnum(probe);
            var list = new List<string>();
            foreach (var (r, remote, _) in WNetChildren(copy.ptr, RESOURCETYPE_DISK, buf, size))
            {
                if (r.dwDisplayType != RESOURCEDISPLAYTYPE_SHARE || remote is null) continue;
                var name = remote.TrimEnd('\\');
                int i = name.LastIndexOf('\\');
                if (i >= 0) name = name.Substring(i + 1);
                if (name.Length > 0 && !name.EndsWith('$')) list.Add(name);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
        finally { Marshal.FreeHGlobal(buf); copy.Free(); }
    }

    private readonly struct ResourceCopy
    {
        public readonly IntPtr ptr;
        private readonly IntPtr _remote, _provider;
        public ResourceCopy(IntPtr p, IntPtr remote, IntPtr provider) { ptr = p; _remote = remote; _provider = provider; }
        public void Free()
        {
            if (_remote != IntPtr.Zero) Marshal.FreeHGlobal(_remote);
            if (_provider != IntPtr.Zero) Marshal.FreeHGlobal(_provider);
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static ResourceCopy CloneResource(NETRESOURCE res, string? remote, string? provider)
    {
        var pRemote = remote is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(remote);
        var pProvider = provider is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(provider);
        res.lpLocalName = IntPtr.Zero;
        res.lpComment = IntPtr.Zero;
        res.lpRemoteName = pRemote;
        res.lpProvider = pProvider;
        var p = Marshal.AllocHGlobal(Marshal.SizeOf<NETRESOURCE>());
        Marshal.StructureToPtr(res, p, false);
        return new ResourceCopy(p, pRemote, pProvider);
    }
}
