using SkiaSharp;

namespace AINEPaint.Brushes;

/// <summary>
/// 1本のストロークを描く。Begin → AddPoint（複数回）→ End の順で使う。
///
/// 【滑らかさ】
/// 入力点をそのまま直線で結ぶと速く描いたときに折れ線になるため、
/// 「隣り合う点の中点どうしを、元の点を制御点にした2次ベジェで繋ぐ」方式にしている。
/// 1点受け取るごとに確定区間だけ描けるので、遅延を挟まずに滑らかにできる。
///
/// 【不透明度】
/// ストロークは必ず専用のバッファへ不透明で描き、
/// 指を離した時点でバッファ全体を1回だけ不透明度を掛けて合成する。
/// こうしないと、同じストローク内で線が重なった箇所だけ濃くなる。
/// 消しゴムも同じバッファを使い、合成時のブレンドモードだけを変える。
/// </summary>
public sealed class StrokeRenderer : IDisposable
{
    /// <summary>これ未満しか動いていない入力点は捨てる（ドキュメントピクセル）。手ブレ対策。</summary>
    private const float MinPointDistance = 0.6f;

    private SKBitmap? _buffer;
    private SKCanvas? _bufferCanvas;
    private SKBitmap? _target;
    private SKPaint? _paint;
    private BrushSettings? _settings;

    private StrokePoint _previous;
    private SKPoint _lastMid;
    private bool _hasSegment;
    private SKRect _previousDirty = SKRect.Empty;

    public bool IsActive => _target is not null;

    /// <summary>描画中のストローク。CanvasView が合成してプレビュー表示する。</summary>
    public SKBitmap? PreviewBuffer => IsActive ? _buffer : null;

    /// <summary>消しゴムは合成方法が違うので、表示側でも区別が要る。</summary>
    public bool IsErasing => _settings?.Kind == BrushKind.Eraser;

    /// <summary>今回のストロークで書き換わった範囲（ドキュメント座標）。</summary>
    public SKRect DirtyRect { get; private set; } = SKRect.Empty;

    public void Begin(SKBitmap target, BrushSettings settings, StrokePoint start)
    {
        Cancel();

        EnsureBuffer(target.Width, target.Height);
        if (_bufferCanvas is null) return;

        _target = target;
        _settings = settings;
        _paint = CreateStrokePaint(settings);
        _previous = start;
        _lastMid = new SKPoint(start.X, start.Y);
        _hasSegment = false;
        DirtyRect = SKRect.Empty;

        // 置いた瞬間に点が残るように、丸を1つ打つ
        float radius = WidthFor(settings, start.Pressure, start.Pressure) * 0.5f;
        using var dot = _paint.Clone();
        dot.Style = SKPaintStyle.Fill;
        _bufferCanvas.DrawCircle(start.X, start.Y, radius, dot);
        Expand(new SKRect(start.X - radius, start.Y - radius, start.X + radius, start.Y + radius), 2f);
    }

    public void AddPoint(StrokePoint point, BrushSettings settings)
    {
        if (_bufferCanvas is null || _paint is null) return;

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
        _bufferCanvas.DrawPath(path, _paint);

        Expand(path.Bounds, width * 0.5f + 2f);

        _lastMid = mid;
        _previous = point;
        _hasSegment = true;
    }

    /// <summary>ストロークを確定し、ドキュメントへ合成する。書き換わった範囲を返す。</summary>
    public SKRect End()
    {
        if (_target is null || _bufferCanvas is null || _buffer is null || _settings is null)
        {
            Cancel();
            return SKRect.Empty;
        }

        // 最後の入力点までは中点で止まっているので、残りを繋いで描き切る
        if (_paint is not null && _hasSegment)
        {
            _bufferCanvas.DrawLine(_lastMid.X, _lastMid.Y, _previous.X, _previous.Y, _paint);
            Expand(new SKRect(
                Math.Min(_lastMid.X, _previous.X), Math.Min(_lastMid.Y, _previous.Y),
                Math.Max(_lastMid.X, _previous.X), Math.Max(_lastMid.Y, _previous.Y)),
                _paint.StrokeWidth * 0.5f + 2f);
        }

        var dirty = DirtyRect;

        if (!dirty.IsEmpty)
        {
            using var canvas = new SKCanvas(_target);
            using var composite = CreateCompositePaint(_settings);
            canvas.Save();
            canvas.ClipRect(dirty);
            canvas.DrawBitmap(_buffer, 0, 0, composite);
            canvas.Restore();
        }

        _previousDirty = dirty;
        Cancel();
        return dirty;
    }

    /// <summary>合成せずに破棄する（ウィンドウ外へ抜けた場合など）。</summary>
    public void Cancel()
    {
        _hasSegment = false;
        _paint?.Dispose();
        _paint = null;
        _settings = null;
        _target = null;
    }

    /// <summary>プレビュー表示用。CanvasView が使う。</summary>
    public SKPaint? CreatePreviewPaint()
        => _settings is null ? null : CreateCompositePaint(_settings);

    private void EnsureBuffer(int width, int height)
    {
        if (_buffer is not null && _buffer.Width == width && _buffer.Height == height)
        {
            // 前回のストロークが残っている範囲だけ消す（全面クリアは重いので避ける）
            if (!_previousDirty.IsEmpty && _bufferCanvas is not null)
            {
                _bufferCanvas.Save();
                _bufferCanvas.ClipRect(_previousDirty);
                _bufferCanvas.Clear(SKColors.Transparent);
                _bufferCanvas.Restore();
                _previousDirty = SKRect.Empty;
            }
            return;
        }

        _bufferCanvas?.Dispose();
        _buffer?.Dispose();

        var info = new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
        _buffer = new SKBitmap(info);
        _bufferCanvas = new SKCanvas(_buffer);
        _bufferCanvas.Clear(SKColors.Transparent);
        _previousDirty = SKRect.Empty;
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

    /// <summary>バッファへ描くときの筆。常に不透明で描く。</summary>
    private static SKPaint CreateStrokePaint(BrushSettings settings)
    {
        var color = settings.Kind == BrushKind.Eraser
            ? SKColors.Black          // 消しゴムは形だけのマスクとして使う
            : settings.Color;

        return new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            Color = color.WithAlpha(255)
        };
    }

    /// <summary>バッファをドキュメントへ合成するときの筆。ここで初めて不透明度が掛かる。</summary>
    private static SKPaint CreateCompositePaint(BrushSettings settings)
    {
        float opacity = Math.Clamp(settings.Opacity, 0f, 1f);

        // 鉛筆はわずかに透ける
        if (settings.Kind == BrushKind.Pencil)
            opacity *= 0.75f;

        return new SKPaint
        {
            Color = SKColors.White.WithAlpha((byte)Math.Clamp(opacity * 255f, 0f, 255f)),
            BlendMode = settings.Kind == BrushKind.Eraser
                ? SKBlendMode.DstOut   // バッファの形の分だけ透明にする
                : SKBlendMode.SrcOver,
            FilterQuality = SKFilterQuality.None
        };
    }

    public void Dispose()
    {
        Cancel();
        _bufferCanvas?.Dispose();
        _bufferCanvas = null;
        _buffer?.Dispose();
        _buffer = null;
    }
}
