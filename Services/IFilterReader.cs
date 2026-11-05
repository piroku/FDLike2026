using System.Runtime.InteropServices;
using System.Text;

namespace FDLike2026.Services;

/// <summary>
/// Windows の IFilter（Windows Search がファイルの中身を読むときの仕組み）でテキストを取り出す。
/// .doc / .xls / .ppt は Office（または Office の IFilter Pack）が入っていれば読める。
/// 入っていなければ LoadIFilter が失敗して null を返すだけで、落ちない。
/// </summary>
public static class IFilterReader
{
    private const int FILTER_E_END_OF_CHUNKS = unchecked((int)0x80041700);
    private const int FILTER_E_NO_MORE_TEXT = unchecked((int)0x80041701);
    private const int FILTER_E_NO_MORE_VALUES = unchecked((int)0x80041702);
    private const int FILTER_E_NO_TEXT = unchecked((int)0x80041705);
    private const int FILTER_S_LAST_TEXT = 0x00041709;

    [Flags]
    private enum IFILTER_INIT : uint
    {
        CANON_PARAGRAPHS = 1, HARD_LINE_BREAKS = 2, CANON_HYPHENS = 4, CANON_SPACES = 8,
        APPLY_INDEX_ATTRIBUTES = 16, APPLY_OTHER_ATTRIBUTES = 32, INDEXING_ONLY = 64, SEARCH_LINKS = 128,
        APPLY_CRAWL_ATTRIBUTES = 256, FILTER_OWNED_VALUE_OK = 512,
    }

    private enum CHUNK_BREAKTYPE : int { NO_BREAK = 0, EOW = 1, EOS = 2, EOP = 3, EOC = 4 }
    [Flags] private enum CHUNKSTATE : int { TEXT = 1, VALUE = 2, FILTER_OWNED_VALUE = 4 }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPSPEC { public uint ulKind; public IntPtr data; }   // union { PROPID propid; LPWSTR lpwstr; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FULLPROPSPEC { public Guid guidPropSet; public PROPSPEC psProperty; }

    [StructLayout(LayoutKind.Sequential)]
    private struct STAT_CHUNK
    {
        public uint idChunk; public CHUNK_BREAKTYPE breakType; public CHUNKSTATE flags; public uint locale;
        public FULLPROPSPEC attribute; public uint idChunkSource; public uint cwcStartSource; public uint cwcLenSource;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILTERREGION { public uint idChunk; public uint cwcStart; public uint cwcExtent; }

    [ComImport, Guid("89BCB740-6119-101A-BCB7-00DD010655AF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFilter
    {
        [PreserveSig] int Init(IFILTER_INIT grfFlags, int cAttributes, IntPtr aAttributes, out int pdwFlags);
        [PreserveSig] int GetChunk(out STAT_CHUNK pStat);
        [PreserveSig] int GetText(ref uint pcwcBuffer, IntPtr awcBuffer);
        [PreserveSig] int GetValue(ref IntPtr ppPropValue);
        [PreserveSig] int BindRegion(FILTERREGION origPos, ref Guid riid, out IntPtr ppunk);
    }

    [DllImport("query.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int LoadIFilter(string pwcsPath, [MarshalAs(UnmanagedType.IUnknown)] object? pUnkOuter, out IFilter? ppIUnk);

    private const int REGDB_E_CLASSNOTREG = unchecked((int)0x80040154);
    private const int FILTER_E_UNKNOWNFORMAT = unchecked((int)0x8004170B);

    /// <summary>IFilter が無い拡張子（登録なしと判った拡張子だけ記憶し、以後は呼ばない）。</summary>
    private static readonly HashSet<string> NoFilter = new(StringComparer.OrdinalIgnoreCase);

    public static string? Extract(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        lock (NoFilter) if (NoFilter.Contains(ext)) return null;

        IFilter? filter = null;
        IntPtr buf = IntPtr.Zero;
        try
        {
            int hr = LoadIFilter(path, null, out filter);
            if (hr != 0 || filter is null)
            {
                if (hr is REGDB_E_CLASSNOTREG or FILTER_E_UNKNOWNFORMAT) lock (NoFilter) NoFilter.Add(ext);
                return null;
            }

            hr = filter.Init(IFILTER_INIT.CANON_PARAGRAPHS | IFILTER_INIT.HARD_LINE_BREAKS | IFILTER_INIT.CANON_SPACES | IFILTER_INIT.APPLY_INDEX_ATTRIBUTES, 0, IntPtr.Zero, out _);
            if (hr != 0) return null;

            const int cap = 16384;
            buf = Marshal.AllocHGlobal(cap * 2);
            var sb = new StringBuilder();
            while (true)
            {
                hr = filter.GetChunk(out var stat);
                if (hr == FILTER_E_END_OF_CHUNKS) break;
                if (hr != 0) continue;   // 読めないチャンクは飛ばす（FILTER_E_EMBEDDING_UNAVAILABLE など）
                if (!stat.flags.HasFlag(CHUNKSTATE.TEXT)) continue;

                switch (stat.breakType)
                {
                    case CHUNK_BREAKTYPE.EOW: if (sb.Length > 0 && sb[^1] != ' ' && sb[^1] != '\n') sb.Append(' '); break;
                    case CHUNK_BREAKTYPE.EOS: if (sb.Length > 0 && sb[^1] != ' ' && sb[^1] != '\n') sb.Append(' '); break;
                    case CHUNK_BREAKTYPE.EOP:
                    case CHUNK_BREAKTYPE.EOC: if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n'); break;
                }
                while (true)
                {
                    uint n = cap;
                    int th = filter.GetText(ref n, buf);
                    if (th == FILTER_E_NO_MORE_TEXT || th == FILTER_E_NO_TEXT) break;
                    if (th != 0 && th != FILTER_S_LAST_TEXT) break;
                    if (n > 0) sb.Append(Marshal.PtrToStringUni(buf, (int)n));
                    if (th == FILTER_S_LAST_TEXT) break;
                }
                if (sb.Length > 50_000_000) break;   // 異常に大きいものは打ち切り
            }
            return sb.ToString();
        }
        catch { return null; }
        finally
        {
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            if (filter is not null) { try { Marshal.ReleaseComObject(filter); } catch { } }
        }
    }
}
