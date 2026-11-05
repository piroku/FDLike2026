using System.IO;

namespace FDLike2026.Services;

/// <summary>
/// 貼り付けられた「パスらしき文字列」を、移動できるフォルダに整える。
/// Teams / メール / ブラウザから来る次のような形をすべて受け付ける:
///   "\\server\share\folder"（引用符付き）、&lt;\\server\share&gt;、file://server/share/folder、file:///C:/Users/…、
///   %20 などの URL エンコード、/ 区切り、末尾の \ や空白・改行、環境変数（%USERPROFILE% 等）。
/// 文字列がファイルを指していれば、そのフォルダへ移動してファイルにカーソルを合わせる。
/// </summary>
public static class PathNormalizer
{
    public sealed record Result(string Dir, string? SelectName);

    /// <summary>整形して存在確認まで行う。フォルダもファイルも見つからなければ null。</summary>
    public static Result? Resolve(string? text)
    {
        var p = Normalize(text);
        if (p.Length == 0) return null;
        if (Directory.Exists(p)) return new Result(p, null);
        if (File.Exists(p)) return new Result(Path.GetDirectoryName(p) ?? p, Path.GetFileName(p));
        // 深い所が無い（ファイル名が消されている等）なら、存在する親まで遡る
        var cur = p;
        for (int i = 0; i < 4; i++)
        {
            var parent = Path.GetDirectoryName(cur);
            if (string.IsNullOrEmpty(parent) || parent == cur) break;
            if (Directory.Exists(parent)) return new Result(parent, Path.GetFileName(cur));
            cur = parent;
        }
        return null;
    }

    /// <summary>存在確認なしの整形だけ。</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        // 複数行なら最初の空でない行
        var s = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        s = s.Trim().Trim('"', '\'', '<', '>', '「', '」', '『', '』').Trim();

        if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            // file:///C:/a/b → C:\a\b、file://server/share → \\server\share、file:/C:/x も許す
            var rest = s.Substring(5).TrimStart('/');
            // ここで rest は "C:/a/b" か "server/share/x"。元が file:///（スラッシュ 3 つ）ならローカル、2 つなら UNC
            int slashes = s.Length - 5 - s.Substring(5).TrimStart('/').Length;
            rest = Uri.UnescapeDataString(rest);
            s = slashes >= 3 || (rest.Length >= 2 && rest[1] == ':') ? rest : @"\\" + rest;
        }
        else if (s.Contains('%'))
        {
            // 環境変数（%USERPROFILE%）はそのまま、%20 のような URL エンコードだけ戻す
            if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"%[A-Za-z_][A-Za-z0-9_()]*%"))
            {
                try { s = Uri.UnescapeDataString(s); } catch { }
            }
        }

        s = Environment.ExpandEnvironmentVariables(s);
        s = s.Replace('/', '\\');
        // 「\\?\」などは触らない。末尾の区切りはルート以外では落とす
        if (s.Length > 3 && s.EndsWith('\\') && !s.EndsWith(":\\")) s = s.TrimEnd('\\');
        return s.Trim();
    }
}
