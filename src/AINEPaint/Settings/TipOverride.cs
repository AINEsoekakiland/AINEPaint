using System.Text.Json.Serialization;

namespace AINEPaint.Settings;

/// <summary>
/// ペン先ごとの調整値。null は「そのペン先の既定値のまま」という意味。
/// 既定値はコード側（TipProfile）にあるので、ここには変えたものだけが入る。
/// </summary>
public sealed class TipOverride
{
    /// <summary>筆圧の効き（0〜1）。</summary>
    [JsonPropertyName("pressure")]
    public float? Pressure { get; set; }

    /// <summary>濃さ（0〜1）。</summary>
    [JsonPropertyName("opacity")]
    public float? Opacity { get; set; }

    /// <summary>ざらつき（0〜1）。</summary>
    [JsonPropertyName("grain")]
    public float? Grain { get; set; }

    public bool IsEmpty => Pressure is null && Opacity is null && Grain is null;
}
