using System.Text.Json.Serialization;

namespace AINEPaint.Settings;

/// <summary>
/// 次回起動時に復元する設定。
///
/// 新しい項目は必ず既定値を持たせること。
/// 古い settings.json を読んでも落ちないようにするため。
/// </summary>
public sealed class AppSettings
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    // ---- ブラシ ----
    [JsonPropertyName("brushSize")]
    public float BrushSize { get; set; } = 12f;

    [JsonPropertyName("brushOpacity")]
    public float BrushOpacity { get; set; } = 1f;

    [JsonPropertyName("brushColor")]
    public string BrushColor { get; set; } = "#000000";

    [JsonPropertyName("fillTolerance")]
    public int FillTolerance { get; set; } = 24;

    [JsonPropertyName("fillExpand")]
    public int FillExpand { get; set; } = 1;

    [JsonPropertyName("presets")]
    public List<BrushPreset> Presets { get; set; } = new();

    // ---- ウィンドウ ----
    [JsonPropertyName("windowWidth")]
    public double WindowWidth { get; set; } = 1280;

    [JsonPropertyName("windowHeight")]
    public double WindowHeight { get; set; } = 800;

    // 「未設定」は null で表す。NaN は JSON に書けないので使わないこと。
    [JsonPropertyName("windowLeft")]
    public double? WindowLeft { get; set; }

    [JsonPropertyName("windowTop")]
    public double? WindowTop { get; set; }

    [JsonPropertyName("windowMaximized")]
    public bool WindowMaximized { get; set; }

    // ---- 履歴 ----
    [JsonPropertyName("undoMaxEntries")]
    public int UndoMaxEntries { get; set; } = 50;

    [JsonPropertyName("undoMaxMegabytes")]
    public int UndoMaxMegabytes { get; set; } = 512;

    // ---- ファイル ----
    [JsonPropertyName("lastFolder")]
    public string LastFolder { get; set; } = "";
}
