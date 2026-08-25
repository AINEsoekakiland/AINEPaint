using SkiaSharp;

namespace AINEPaint.Brushes;

/// <summary>
/// 1本のストロークをビットマップへ描き込む。
/// Begin → AddPoint（複数回）→ End の順で使う。
///
/// 現状は対象ビットマップへ直接描く。
/// 不透明度を下げたときに重なりが濃くなる問題は、
/// STEP 8 で「ストローク用の一時レイヤーに描いてから合成する」方式に
/// 差し替えて解決する。呼び出し側の使い方は変えずに済む設計にしてある。
/// </summary>
public sealed class StrokeRenderer : IDisposable
{
    private SKCanvas? _canvas;
    private SKPaint? _paint;
    private StrokePoint _last;

    public bool IsActive => _canvas is not null;

    /// <summary>今回のストロークで書き換わった範囲（ドキュメント座標）。</summary>
    public SKRect DirtyRect { get; private set; } = SKRect.Empty;

    public void Begin(SKBitmap target, BrushSettings settings, StrokePoint start)
    {
        End();

        _canvas = new SKCanvas(target);
        _paint = CreatePaint(settings);
        _last = start;
        DirtyRect = SKRect.Empty;

        // 押した瞬間に点が残るように、同じ座標へ極小の線分を引く
        DrawSegment(start, start, settings);
    }

    public void AddPoint(StrokePoint point, BrushSettings settings)
    {
        if (_canvas is null) return;
        DrawSegment(_last, point, settings);
        _last = point;
    }

    public void End()
    {
        _paint?.Dispose();
        _paint = null;
        _canvas?.Dispose();
        _canvas = null;
    }

    private void DrawSegment(StrokePoint a, StrokePoint b, BrushSettings settings)
    {
        if (_canvas is null || _paint is null) return;

        // 筆圧はまだ線幅にのみ反映する。マウスなら Pressure = 1.0 で一定。
        float pressure = Math.Clamp((a.Pressure + b.Pressure) * 0.5f, 0.05f, 1f);
        float width = Math.Max(0.5f, settings.Size * pressure);
        _paint.StrokeWidth = width;

        _canvas.DrawLine(a.X, a.Y, b.X, b.Y, _paint);

        float pad = width * 0.5f + 2f;
        var segment = new SKRect(
            Math.Min(a.X, b.X) - pad,
            Math.Min(a.Y, b.Y) - pad,
            Math.Max(a.X, b.X) + pad,
            Math.Max(a.Y, b.Y) + pad);

        DirtyRect = DirtyRect.IsEmpty ? segment : SKRect.Union(DirtyRect, segment);
    }

    private static SKPaint CreatePaint(BrushSettings settings)
    {
        byte alpha = (byte)Math.Clamp(settings.Opacity * 255f, 0f, 255f);

        var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = settings.Color.WithAlpha(alpha)
        };

        switch (settings.Kind)
        {
            case BrushKind.Pencil:
                // 鉛筆風: アンチエイリアスを弱め、わずかに透ける
                paint.IsAntialias = true;
                paint.Color = settings.Color.WithAlpha((byte)(alpha * 0.75f));
                break;

            case BrushKind.Eraser:
                // 透明で塗りつぶす = 消しゴム
                paint.IsAntialias = true;
                paint.BlendMode = SKBlendMode.Clear;
                paint.Color = SKColors.Transparent.WithAlpha(alpha);
                break;

            default:
                paint.IsAntialias = true;
                break;
        }

        return paint;
    }

    public void Dispose() => End();
}
