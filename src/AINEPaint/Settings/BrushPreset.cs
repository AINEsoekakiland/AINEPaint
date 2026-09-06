using System.Text.Json.Serialization;

namespace AINEPaint.Settings;

/// <summary>登録したブラシ設定。設定ファイルにそのまま書き出す形。</summary>
public sealed class BrushPreset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "ブラシ";

    /// <summary>BrushKind の名前（"Pen" / "GPen" / "Marker" / "Brush" / "Airbrush" / "Crayon" / "Pencil" / "Eraser"）。</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "Pen";

    [JsonPropertyName("size")]
    public float Size { get; set; } = 12f;

    [JsonPropertyName("opacity")]
    public float Opacity { get; set; } = 1f;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#000000";
}
