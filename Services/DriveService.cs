using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FDLike2026.Services;

public sealed record DriveItem(string Letter, string Root, DriveType Type, string Label, bool IsNetwork);

/// <summary>
/// ドライブ一覧。切断されたネットワークドライブに対して IsReady / VolumeLabel を呼ぶと
/// 数十秒ブロックするため、ネットワークドライブは UNC パスだけを（ブロックせずに）取得する。
/// </summary>
public static class DriveService
{
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

    public static string? GetUncPath(string letter)
    {
        var sb = new StringBuilder(1024);
        int len = sb.Capacity;
        return WNetGetConnection(letter.TrimEnd('\\'), sb, ref len) == 0 ? sb.ToString() : null;
    }

    /// <summary>ブロックしない範囲で即座に一覧を返す。ローカルドライブのラベルは後から <see cref="LoadLabelAsync"/> で取得する。</summary>
    public static List<DriveItem> QuickList()
    {
        var list = new List<DriveItem>();
        foreach (var d in DriveInfo.GetDrives())
        {
            var letter = d.Name.TrimEnd('\\');
            bool net = d.DriveType == DriveType.Network;
            string label = net
                ? (GetUncPath(letter) ?? "ネットワーク ドライブ")
                : TypeName(d.DriveType);
            list.Add(new DriveItem(letter, d.RootDirectory.FullName, d.DriveType, label, net));
        }
        return list;
    }

    /// <summary>ローカルドライブのボリュームラベルをバックグラウンドで取得（タイムアウト付き）。</summary>
    public static async Task<string?> LoadLabelAsync(DriveItem item, int timeoutMs = 3000)
    {
        if (item.IsNetwork) return null;
        var task = Task.Run(() =>
        {
            try
            {
                var d = new DriveInfo(item.Root);
                return d.IsReady && !string.IsNullOrEmpty(d.VolumeLabel) ? d.VolumeLabel : null;
            }
            catch { return null; }
        });
        var done = await Task.WhenAny(task, Task.Delay(timeoutMs));
        return done == task ? task.Result : null;
    }

    public static async Task<long?> LoadFreeAsync(DriveItem item, int timeoutMs = 3000)
    {
        var task = Task.Run(() =>
        {
            try { var d = new DriveInfo(item.Root); return d.IsReady ? d.AvailableFreeSpace : (long?)null; }
            catch { return null; }
        });
        var done = await Task.WhenAny(task, Task.Delay(timeoutMs));
        return done == task ? task.Result : null;
    }

    public static string TypeName(DriveType t) => t switch
    {
        DriveType.Fixed => "ローカル ディスク",
        DriveType.Removable => "リムーバブル ディスク",
        DriveType.Network => "ネットワーク ドライブ",
        DriveType.CDRom => "CD/DVD ドライブ",
        DriveType.Ram => "RAM ディスク",
        _ => "ドライブ",
    };
}
