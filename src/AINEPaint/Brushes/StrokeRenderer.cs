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
///
/// 【太さ】
/// 区間ごとに1つの線幅で描くと、筆圧が変わったとき線が「太さの違う棒の連結」になり、
/// 払いが階段状のかたまりに見える。そのため区間を細かく刻み、
/// 太さを連続的に変えながら円を並べて描いている（スタンプ方式）。
/// 筆圧そのものも生値は細かく揺れるので、移動平均で均してから使う。
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
    private SKPath? _clip;

    private StrokePoint _previous;
    private SKPoint _lastMid;
    private float _lastWidth;
    private float _smoothPressure = 1f;
    private bool _hasSegment;
    private SKRect _previousDirty = SKRect.Empty;

    public bool IsActive => _target is not null;

    /// <summary>描画中のストローク。CanvasView が合成してプレビュー表示する。</summary>
    public SKBitmap? PreviewBuffer => IsActive ? _buffer : null;

    /// <summary>消しゴムは合成方法が違うので、表示側でも区別が要る。</summary>
    public bool IsErasing => _settings?.Kind == BrushKind.Eraser;

    /// <summary>今回のストロークで書き換わった範囲（ドキュメント座標）。</summary>
    public SKRect DirtyRect { get; private set; } = SKRect.Empty;

    public void Begin(SKBitmap target, BrushSettings settings, StrokePoint start, SKPath? clip = null)
    {
        Cancel();

        // 選択範囲があるときは、合成時にその中だけへ書き込む
        _clip = clip;

        EnsureBuffer(target.Width, target.Height);
        if (_bufferCanvas is null) return;

        _target = target;
        _settings = settings;
        _paint = CreateStrokePaint(settings);
        _previous = start;
        _lastMid = new SKPoint(start.X, start.Y);
        _hasSegment = false;
        _smoothPressure = Math.Clamp(start.Pressure, 0.01f, 1f);
        _lastWidth = WidthFor(settings, _smoothPressure);
        DirtyRect = SKRect.Empty;

        // 置いた瞬間に点が残るように、丸を1つ打つ
        float radius = _lastWidth * 0.5f;
        _bufferCanvas.DrawCircle(start.X, start.Y, radius, _paint);
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

        // 筆圧の生値は1点ごとに細かく揺れる。そのまま太さにすると線がガタつく。
        _smoothPressure += (Math.Clamp(point.Pressure, 0.01f, 1f) - _smoothPressure) * PressureSmoothing;
        float width = WidthFor(settings, _smoothPressure);

        StampQuad(_lastMid, new SKPoint(_previous.X, _previous.Y), mid, _lastWidth, width);

        _lastMid = mid;
        _lastWidth = width;
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
            var end = new SKPoint(_previous.X, _previous.Y);
            var control = new SKPoint((_lastMid.X + end.X) * 0.5f, (_lastMid.Y + end.Y) * 0.5f);
            StampQuad(_lastMid, control, end, _lastWidth, _lastWidth);
        }

        var dirty = DirtyRect;

        if (!dirty.IsEmpty)
        {
            using var canvas = new SKCanvas(_target);
            using var composite = CreateCompositePaint(_settings);
            canvas.Save();
            canvas.ClipRect(dirty);
            if (_clip is not null)
                canvas.ClipPath(_clip, SKClipOperation.Intersect, antialias: true);
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
        _clip = null;
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

    /// <summary>筆圧の追従の速さ。小さいほど滑らかになるが、反応は鈍くなる。</summary>
    private const float PressureSmoothing = 0.35f;

    /// <summary>円を並べる間隔。太さに対する割合。小さいほど滑らかで、そのぶん重い。</summary>
    private const float StampSpacingRatio = 0.10f;

    /// <summary>1区間あたりの円の数の上限。拡大表示で極端に増えるのを防ぐ。</summary>
    private const int MaxStampsPerSegment = 512;

    /// <summary>
    /// 2次ベジェの区間を、太さを w0 から w1 へ変えながら円で埋める。
    /// 円は不透明で重ねて描くので、重なっても濃くはならない（不透明度は合成時に一度だけ掛ける）。
    /// </summary>
    private void StampQuad(SKPoint p0, SKPoint control, SKPoint p1, float w0, float w1)
    {
        if (_bufferCanvas is null || _paint is null) return;

        float length = Distance(p0, control) + Distance(control, p1);
        float spacing = Math.Max(0.35f, Math.Max(w0, w1) * StampSpacingRatio);
        int steps = Math.Clamp((int)MathF.Ceiling(length / spacing), 1, MaxStampsPerSegment);

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            float u = 1f - t;

            float x = u * u * p0.X + 2f * u * t * control.X + t * t * p1.X;
            float y = u * u * p0.Y + 2f * u * t * control.Y + t * t * p1.Y;
            float radius = Math.Max(0.25f, (w0 + (w1 - w0) * t) * 0.5f);

            _bufferCanvas.DrawCircle(x, y, radius, _paint);
        }

        float pad = Math.Max(w0, w1) * 0.5f + 2f;
        var bounds = new SKRect(
            Math.Min(p0.X, Math.Min(control.X, p1.X)), Math.Min(p0.Y, Math.Min(control.Y, p1.Y)),
            Math.Max(p0.X, Math.Max(control.X, p1.X)), Math.Max(p0.Y, Math.Max(control.Y, p1.Y)));
        Expand(bounds, pad);
    }

    private static float Distance(SKPoint a, SKPoint b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// 筆圧を線幅へ変換する。
    /// そのまま比例させると軽く触れた部分が太く残って払いが鈍るので、
    /// 弱い側をより細くする補正を掛けている。
    /// </summary>
    private static float WidthFor(BrushSettings settings, float pressure)
    {
        float p = Math.Clamp(pressure, 0.01f, 1f);
        float curved = MathF.Pow(p, 1.4f);
        return Math.Max(0.35f, settings.Size * curved);
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

        // 円を並べて描くので Fill。線幅は使わない。
        return new SKPaint
        {
            Style = SKPaintStyle.Fill,
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
