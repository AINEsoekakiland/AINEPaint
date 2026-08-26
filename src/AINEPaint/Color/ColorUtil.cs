using System.Globalization;
using SkiaSharp;

namespace AINEPaint.Color;

/// <summary>
/// 色の文字列表現まわり。HSV 変換は SkiaSharp の SKColor.FromHsv / ToHsv を使うので
/// ここには持たない。カラーホイールを足す場合もこの方針を変えなくて済む。
/// </summary>
public static class ColorUtil
{
    public static string ToHex(SKColor color)
        => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    /// <summary>"#RRGGBB" / "RRGGBB" / "#RGB" を受け付ける。</summary>
    public static bool TryParseHex(string? text, out SKColor color)
    {
        color = SKColors.Black;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim().TrimStart('#');

        if (s.Length == 3)
            s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";

        if (s.Length != 6) return false;

        if (!byte.TryParse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) ||
            !byte.TryParse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) ||
            !byte.TryParse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            return false;

        color = new SKColor(r, g, b);
        return true;
    }

    public static System.Windows.Media.Color ToWpf(SKColor color)
        => System.Windows.Media.Color.FromRgb(color.Red, color.Green, color.Blue);
}
