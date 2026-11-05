using System.Text;

namespace FDLike2026.Services;

/// <summary>
/// テキストファイルの文字コード判定とデコード（ビューアと grep で共用）。
/// UTF-16 / UTF-32（BOM あり / なし）→ UTF-8（BOM 有無）→ Shift_JIS の順。
/// </summary>
public static class TextDecoder
{
    static TextDecoder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>0x00 を含むなどテキストに見えない（UTF-16 を除く）。</summary>
    public static bool LooksBinary(byte[] b, int count = -1)
    {
        if (count < 0) count = b.Length;
        if (DetectUnicode(b, count) is not null) return false;
        int n = Math.Min(count, 4096);
        for (int i = 0; i < n; i++) if (b[i] == 0) return true;
        return false;
    }

    /// <summary>UTF-16 / UTF-32 の BOM、または BOM なし UTF-16 らしさ（偶数 or 奇数位置の大半が 0）を判定。</summary>
    public static Encoding? DetectUnicode(byte[] b, int count = -1)
    {
        if (count < 0) count = b.Length;
        if (count >= 2 && b[0] == 0xFF && b[1] == 0xFE)
        {
            if (count >= 4 && b[2] == 0 && b[3] == 0) return new UTF32Encoding(false, true);
            return new UnicodeEncoding(false, true);
        }
        if (count >= 2 && b[0] == 0xFE && b[1] == 0xFF) return new UnicodeEncoding(true, true);
        if (count >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 0xFE && b[3] == 0xFF) return new UTF32Encoding(true, true);

        int n = Math.Min(count, 4096) & ~1;
        if (n < 8) return null;
        int oddZero = 0, evenZero = 0;
        for (int i = 0; i < n; i += 2) { if (b[i] == 0) evenZero++; if (b[i + 1] == 0) oddZero++; }
        int pairs = n / 2;
        if (oddZero > pairs * 0.7 && evenZero < pairs * 0.1) return new UnicodeEncoding(false, false);
        if (evenZero > pairs * 0.7 && oddZero < pairs * 0.1) return new UnicodeEncoding(true, false);
        return null;
    }

    /// <summary>バイト列を最適な文字コードで文字列にする。</summary>
    public static string Decode(byte[] b, int count = -1)
    {
        if (count < 0) count = b.Length;
        if (DetectUnicode(b, count) is { } uni)
        {
            try { return uni.GetString(b, 0, count).TrimStart('﻿'); } catch { }
        }
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            return utf8.GetString(b, 0, count).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(932).GetString(b, 0, count);
        }
    }
}
