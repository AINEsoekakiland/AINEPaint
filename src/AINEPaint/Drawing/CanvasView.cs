using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace AINEPaint.Drawing;

/// <summary>
/// キャンバス表示領域。
/// STEP 5 の時点では「SkiaSharp が WPF 上で描画できること」の確認のみを行う。
/// 実際のキャンバス生成・レイヤー合成・ブラシ描画は次のステップ以降で
/// このクラスの内部（および Layers / Brushes）に実装していく。
/// </summary>
public class CanvasView : SKElement
{
    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;

        // ワークエリアの背景（キャンバスそのものではない）
        canvas.Clear(new SKColor(0x14, 0x14, 0x14));

        // Skia が実際に描けているかを目視確認するための一時的な表示。
        // 次のステップで本物のキャンバス描画に置き換える。
        var info = e.Info;
        using var paint = new SKPaint
        {
            Color = new SKColor(0x4E, 0xA1, 0xFF),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2
        };

        float cx = info.Width / 2f;
        float cy = info.Height / 2f;
        canvas.DrawCircle(cx, cy, 40, paint);
    }
}
