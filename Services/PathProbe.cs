using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace FDLike2026.Services;

/// <summary>
/// 切断されたネットワークドライブ / 共有フォルダに触って固まらないための到達確認。
/// Directory.Exists や DriveInfo.IsReady は相手先が停止していると SMB のタイムアウト（数十秒）まで
/// 同期でブロックするので、ネットワーク上のパスは先にホストの 445 番ポートへ短いタイムアウトで接続を試し、
/// 届かなければ「無い」扱いにする。ローカルのパスはそのまま Directory.Exists を呼ぶ。
/// </summary>
public static class PathProbe
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDriveTypeW(string lpRootPathName);

    private const uint DRIVE_REMOTE = 4;

    /// <summary>ホストごとの到達結果（数秒だけ覚えて連続アクセスで何度も待たないようにする）。</summary>
    private static readonly Dictionary<string, (bool ok, DateTime at)> HostCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Lock = new();

    public const int DefaultTimeoutMs = 1500;

    /// <summary>UNC パス、またはネットワークドライブ上のパスなら true（ネットワークへはアクセスしない）。</summary>
    public static bool IsNetworkPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':') return false;
        try { return GetDriveTypeW(root.TrimEnd('\\') + "\\") == DRIVE_REMOTE; }
        catch { return false; }
    }

    /// <summary>パスのホスト名（UNC の先頭、ネットワークドライブは割り当て先の UNC から）。分からなければ null。</summary>
    public static string? HostOf(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string unc = path;
        if (!path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || root.Length < 2) return null;
            unc = DriveService.GetUncPath(root.Substring(0, 2)) ?? "";
        }
        var parts = unc.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var host = parts[0];
        if (host.EndsWith("@SSL", StringComparison.OrdinalIgnoreCase)) host = host[..^4];   // WebDAV の表記
        return host.Length == 0 ? null : host;
    }

    /// <summary>ホストの 445 番（SMB）へ短いタイムアウトで接続できるか。結果は 10 秒キャッシュ。</summary>
    public static bool IsHostReachable(string host, int timeoutMs = DefaultTimeoutMs)
    {
        lock (Lock)
        {
            if (HostCache.TryGetValue(host, out var c) && (DateTime.Now - c.at).TotalSeconds < 10) return c.ok;
        }
        bool ok = false;
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(host, 445);
            ok = task.Wait(timeoutMs) && client.Connected;
        }
        catch { ok = false; }
        lock (Lock) HostCache[host] = (ok, DateTime.Now);
        return ok;
    }

    /// <summary>ネットワーク上のパスで、相手先に届かないなら true。ローカルなら常に false。</summary>
    public static bool IsUnreachable(string? path, int timeoutMs = DefaultTimeoutMs)
    {
        if (!IsNetworkPath(path)) return false;
        var host = HostOf(path);
        if (host is null) return false;   // ホストが分からなければ普通に試す
        return !IsHostReachable(host, timeoutMs);
    }

    /// <summary>キャッシュを捨てる（F5 など、改めて試したいとき）。</summary>
    public static void Reset() { lock (Lock) HostCache.Clear(); }

    /// <summary>
    /// 固まらない Directory.Exists。ネットワーク上なら先にホストの到達確認をし、届いていても
    /// <paramref name="timeoutMs"/> を超えたら false（呼び出しは裏で続くが UI は待たない）。
    /// </summary>
    public static bool DirectoryExists(string? path, int timeoutMs = 3000)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (!IsNetworkPath(path))
        {
            try { return Directory.Exists(path); } catch { return false; }
        }
        if (IsUnreachable(path)) return false;
        try
        {
            var task = Task.Run(() => Directory.Exists(path));
            return task.Wait(timeoutMs) && task.Result;
        }
        catch { return false; }
    }
}
