using System.Text.Json.Serialization;

namespace AINEPaint.Settings;

/// <summary>
/// ツール（ペン / 鉛筆 / 消しゴム）ごとに覚えておく太さと不透明度。
/// 1つの値を全ツールで共有すると、消しゴムの太さがペンにも移ってしまうため分けている。
/// </summary>
public sealed class ToolBrushState
{
    [JsonPropertyName("size")]
    public float Size { get; set; } = 12f;

    /// <summary>0.0〜1.0。</summary>
    [JsonPropertyName("opacity")]
    public float Opacity { get; set; } = 1f;
}
