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

    public SKColor Color { get; set; } = SKColors.Black;
}
