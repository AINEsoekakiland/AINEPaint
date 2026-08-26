using System.Text.Json.Serialization;

namespace AINEPaint.IO;

/// <summary>
/// .ainpaint の中に入る project.json の形。
///
/// 項目を増やしても古いファイルが読めなくなることのないよう、
/// 新しい項目は必ず既定値を持たせる。FormatVersion は読み込み時の判断に使う。
/// </summary>
public sealed class ProjectManifest
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("application")]
    public string Application { get; set; } = "AINE Paint";

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    /// <summary>"white" または "transparent"</summary>
    [JsonPropertyName("background")]
    public string Background { get; set; } = "white";

    [JsonPropertyName("activeLayerIndex")]
    public int ActiveLayerIndex { get; set; }

    /// <summary>下から順に並べる。</summary>
    [JsonPropertyName("layers")]
    public List<LayerManifest> Layers { get; set; } = new();
}

public sealed class LayerManifest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "レイヤー";

    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = true;

    [JsonPropertyName("opacity")]
    public float Opacity { get; set; } = 1f;

    /// <summary>ZIP 内の画像ファイルのパス。</summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = "";
}
