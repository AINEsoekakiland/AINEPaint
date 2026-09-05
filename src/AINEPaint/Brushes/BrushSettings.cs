using SkiaSharp;

namespace AINEPaint.Brushes;

public enum BrushKind
{
    Pen,
    Pencil,
    Eraser
}

/// <summary>
/// ブラシの設定値。UI（下部バー）とレンダラの間を繋ぐ唯一の受け渡し口。
/// プリセット保存（Phase 2）はこのクラスを直列化すれば済むようにしておく。
/// </summary>
public sealed class BrushSettings
{
    public BrushKind Kind { get; set; } = BrushKind.Pen;

    /// <summary>直径（ドキュメントピクセル）。</summary>
    public float Size { get; set; } = 12f;

    /// <summary>0.0〜1.0。</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>
    /// 筆圧を線の太さに反映するか。false ならペンタブでも太さは一定。
    /// マウスでは元から筆圧が来ないので、この値に関わらず一定。
    /// </summary>
    public bool UsePressure { get; set; } = true;

    public SKColor Color { get; set; } = SKColors.Black;

    /// <summary>塗りつぶしの色の許容差（0〜255）。大きいほど広い範囲が塗られる。</summary>
    public int FillTolerance { get; set; } = 24;

    /// <summary>塗りつぶした範囲を外へ広げる量（ピクセル）。線の縁の隙間を埋める。</summary>
    public int FillExpand { get; set; } = 1;
}
