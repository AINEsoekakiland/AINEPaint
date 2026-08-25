using SkiaSharp;

namespace AINEPaint.Drawing;

/// <summary>
/// キャンバスの表示状態（拡大率とスクロール位置）だけを受け持つ。
/// 座標系は「デバイスピクセル」で統一する。DPI変換は CanvasView 側の責務。
///
/// 画面座標 = ドキュメント座標 * Scale + Offset
/// </summary>
public sealed class Viewport
{
    public const float MinScale = 0.02f;
    public const float MaxScale = 32f;

    public float Scale { get; private set; } = 1f;
    public float OffsetX { get; private set; }
    public float OffsetY { get; private set; }

    /// <summary>表示状態が変わったときに発火。再描画のトリガに使う。</summary>
    public event Action? Changed;

    /// <summary>ビュー全体に収まる倍率にして中央へ。</summary>
    public void FitToView(int docWidth, int docHeight, float viewWidth, float viewHeight, float padding = 48f)
    {
        if (docWidth <= 0 || docHeight <= 0 || viewWidth <= 0 || viewHeight <= 0)
            return;

        float sx = (viewWidth - padding * 2f) / docWidth;
        float sy = (viewHeight - padding * 2f) / docHeight;
        Scale = Math.Clamp(Math.Min(sx, sy), MinScale, MaxScale);

        Center(docWidth, docHeight, viewWidth, viewHeight);
    }

    /// <summary>倍率は変えずに中央へ寄せる。</summary>
    public void Center(int docWidth, int docHeight, float viewWidth, float viewHeight)
    {
        OffsetX = (viewWidth - docWidth * Scale) * 0.5f;
        OffsetY = (viewHeight - docHeight * Scale) * 0.5f;
        Changed?.Invoke();
    }

    public void Pan(float dx, float dy)
    {
        if (dx == 0f && dy == 0f) return;
        OffsetX += dx;
        OffsetY += dy;
        Changed?.Invoke();
    }

    /// <summary>
    /// 指定した画面座標を固定したまま拡大縮小する。
    /// マウスカーソル位置を基準にズームするために使う。
    /// </summary>
    public void ZoomAt(float anchorX, float anchorY, float factor)
    {
        float newScale = Math.Clamp(Scale * factor, MinScale, MaxScale);
        if (Math.Abs(newScale - Scale) < 1e-6f) return;

        float k = newScale / Scale;
        OffsetX = anchorX - (anchorX - OffsetX) * k;
        OffsetY = anchorY - (anchorY - OffsetY) * k;
        Scale = newScale;
        Changed?.Invoke();
    }

    public SKMatrix Matrix => SKMatrix.CreateScaleTranslation(Scale, Scale, OffsetX, OffsetY);

    /// <summary>画面座標 → ドキュメント座標。STEP 7 のブラシで使う。</summary>
    public SKPoint ToDocument(float viewX, float viewY)
        => new((viewX - OffsetX) / Scale, (viewY - OffsetY) / Scale);
}
