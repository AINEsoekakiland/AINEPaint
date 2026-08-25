using SkiaSharp;

namespace AINEPaint.Brushes;

/// <summary>
/// 1本のストロークをビットマップへ描き込む。
/// Begin → AddPoint（複数回）→ End の順で使う。
///
/// 入力点をそのまま直線で結ぶと、速く描いたときに折れ線になる。
/// そこで「隣り合う点の中点どうしを、元の点を制御点にした2次ベジェで繋ぐ」
/// 方式で滑らかにしている。追加の遅延やバッファは不要で、
/// 1点受け取るごとに確定した区間だけを描けるため描画の追従が落ちない。
///
/// 不透明度を下げたときに重なりが濃くなる問題は、
/// STEP 8 で「ストローク用の一時レイヤーに描いてから合成する」方式に
/// 差し替えて解決する。呼び出し側の使い方は変えずに済む設計にしてある。
/// </summary>
public sealed class StrokeRenderer : IDisposable
{
    /// <summary>これ未満しか動いていない入力点は捨てる（ドキュメントピクセル）。手ブレ対策。</summary>
    private const float MinPointDistance = 0.6f;

    private SKCanvas? _canvas;
    private SKPaint? _paint;

    private StrokePoint _previous;
    private SKPoint _lastMid;
    private bool _hasSegment;

    public bool IsActive => _canvas is not null;

    /// <summary>今回のストロークで書き換わった範囲（ドキュメント座標）。</summary>
    public SKRect DirtyRect { get; private set; } = SKRect.Empty;

    public void Begin(SKBitmap target, BrushSettings settings, StrokePoint start)
    {
        End();

        _canvas = new SKCanvas(target);
        _paint = CreatePaint(settings);
        _previous = start;
        _lastMid = new SKPoint(start.X, start.Y);
        _hasSegment = false;
        DirtyRect = SKRect.Empty;

        // 置いた瞬間に点が残るように、丸を1つ打つ
        float radius = WidthFor(settings, start.Pressure, start.Pressure) * 0.5f;
        using var dot = _paint.Clone();
        dot.Style = SKPaintStyle.Fill;
        _canvas.DrawCircle(start.X, start.Y, radius, dot);
        Expand(new SKRect(start.X - radius, start.Y - radius, start.X + radius, start.Y + radius), 2f);
    }

    public void AddPoint(StrokePoint point, BrushSettings settings)
    {
        if (_canvas is null || _paint is null) return;

        float dx = point.X - _previous.X;
        float dy = point.Y - _previous.Y;
        if (dx * dx + dy * dy < MinPointDistance * MinPointDistance)
            return;

        var mid = new SKPoint((_previous.X + point.X) * 0.5f, (_previous.Y + point.Y) * 0.5f);

        float width = WidthFor(settings, _previous.Pressure, point.Pressure);
        _paint.StrokeWidth = width;

        using var path = new SKPath();
        path.MoveTo(_lastMid);
        path.QuadTo(_previous.X, _previous.Y, mid.X, mid.Y);
        _canvas.DrawPath(path, _paint);

        Expand(path.Bounds, width * 0.5f + 2f);

        _lastMid = mid;
        _previous = point;
        _hasSegment = true;
    }

    public void End()
    {
        // 最後の入力点までは中点で止まっているので、残りを繋いで描き切る
        if (_canvas is not null && _paint is not null && _hasSegment)
        {
            _canvas.DrawLine(_lastMid.X, _lastMid.Y, _previous.X, _previous.Y, _paint);
            Expand(new SKRect(
                Math.Min(_lastMid.X, _previous.X), Math.Min(_lastMid.Y, _previous.Y),
                Math.Max(_lastMid.X, _previous.X), Math.Max(_lastMid.Y, _previous.Y)),
                _paint.StrokeWidth * 0.5f + 2f);
        }

        _hasSegment = false;
        _paint?.Dispose();
        _paint = null;
        _canvas?.Dispose();
        _canvas = null;
    }

    /// <summary>筆圧は今のところ線幅にのみ反映する。マウスでは Pressure = 1.0 で一定。</summary>
    private static float WidthFor(BrushSettings settings, float pressureA, float pressureB)
    {
        float pressure = Math.Clamp((pressureA + pressureB) * 0.5f, 0.05f, 1f);
        return Math.Max(0.5f, settings.Size * pressure);
    }

    private void Expand(SKRect rect, float padding)
    {
        var padded = new SKRect(rect.Left - padding, rect.Top - padding,
                                rect.Right + padding, rect.Bottom + padding);
        DirtyRect = DirtyRect.IsEmpty ? padded : SKRect.Union(DirtyRect, padded);
    }

    private static SKPaint CreatePaint(BrushSettings settings)
    {
        byte alpha = (byte)Math.Clamp(settings.Opacity * 255f, 0f, 255f);

        var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            Color = settings.Color.WithAlpha(alpha)
        };

        switch (settings.Kind)
        {
            case BrushKind.Pencil:
                // 鉛筆風: わずかに透ける
                paint.Color = settings.Color.WithAlpha((byte)(alpha * 0.75f));
                break;

            case BrushKind.Eraser:
                // 透明で塗りつぶす = 消しゴム
                paint.BlendMode = SKBlendMode.Clear;
                paint.Color = SKColors.Transparent.WithAlpha(alpha);
                break;
        }

        return paint;
    }

    public void Dispose() => End();
}
