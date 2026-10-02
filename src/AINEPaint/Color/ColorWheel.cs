using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace AINEPaint.Color;

/// <summary>
/// 色を選ぶ輪。
///
/// 外側の輪で色合い（色相）を、内側の四角で鮮やかさと明るさを選ぶ。
/// 輪と四角を別々の部品にすると位置合わせが面倒になるので、1つにまとめている。
///
/// 輪の中に四角を置く形は、お絵描きソフトで広く使われている配置。
/// 四角は傾けずに正立させ、色づかいはこのソフトの暗い配色に合わせてある。
/// </summary>
public class ColorWheel : SKElement
{
    /// <summary>輪の太さ。短い方の辺に対する割合。</summary>
    private const float RingRatio = 0.14f;

    /// <summary>輪と四角のあいだの隙間（画面ピクセル）。</summary>
    private const float Gap = 10f;

    private float _hue;
    private float _saturation = 1f;
    private float _value = 1f;

    private enum DragTarget { None, Ring, Square }
    private DragTarget _drag = DragTarget.None;

    public event Action? SelectionChanged;

    public ColorWheel()
    {
        Cursor = Cursors.Hand;
    }

    /// <summary>0〜360</summary>
    public float Hue
    {
        get => _hue;
        set { _hue = ((value % 360f) + 360f) % 360f; InvalidateVisual(); }
    }

    /// <summary>0〜1</summary>
    public float Saturation => _saturation;

    /// <summary>0〜1</summary>
    public float Value => _value;

    public void SetSaturationValue(float saturation, float value)
    {
        _saturation = Math.Clamp(saturation, 0f, 1f);
        _value = Math.Clamp(value, 0f, 1f);
        InvalidateVisual();
    }

    // ===== 描画 =====

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var layout = Layout(e.Info.Width, e.Info.Height);

        DrawRing(canvas, layout);
        DrawSquare(canvas, layout);
    }

    private readonly record struct WheelLayout(
        SKPoint Center, float OuterRadius, float RingWidth, SKRect Square, float Scale);

    /// <summary>
    /// 輪と四角の位置を求める。
    /// 画面の拡大率（DPI）で実際の画素数が変わるので、入力座標との換算用に倍率も返す。
    /// </summary>
    private WheelLayout Layout(int pixelWidth, int pixelHeight)
    {
        float scale = ActualWidth > 0 ? (float)(pixelWidth / ActualWidth) : 1f;

        float size = Math.Min(pixelWidth, pixelHeight);
        var center = new SKPoint(pixelWidth * 0.5f, pixelHeight * 0.5f);

        float outer = size * 0.5f - 2f * scale;
        float ring = size * RingRatio;
        float inner = outer - ring - Gap * scale;

        // 円に内接する正方形の一辺
        float half = inner / MathF.Sqrt(2f);
        var square = new SKRect(center.X - half, center.Y - half, center.X + half, center.Y + half);

        return new WheelLayout(center, outer, ring, square, scale);
    }

    private void DrawRing(SKCanvas canvas, WheelLayout layout)
    {
        var colors = new SKColor[13];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = SKColor.FromHsv(i * 30f % 360f, 100, 100);

        // 色相0を真上に置きたいので、輪を90度戻して描く
        var rotate = SKMatrix.CreateRotationDegrees(-90f, layout.Center.X, layout.Center.Y);

        float radius = layout.OuterRadius - layout.RingWidth * 0.5f;

        using (var shader = SKShader.CreateSweepGradient(layout.Center, colors, null, rotate))
        using (var paint = new SKPaint
        {
            Shader = shader,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = layout.RingWidth,
            IsAntialias = true
        })
            canvas.DrawCircle(layout.Center, radius, paint);

        using (var edge = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 1f * layout.Scale,
            Color = new SKColor(0x00, 0x00, 0x00, 0x60), IsAntialias = true
        })
        {
            canvas.DrawCircle(layout.Center, layout.OuterRadius, edge);
            canvas.DrawCircle(layout.Center, layout.OuterRadius - layout.RingWidth, edge);
        }

        // いま選んでいる色相の位置に印
        float angle = (_hue - 90f) * MathF.PI / 180f;
        var marker = new SKPoint(
            layout.Center.X + MathF.Cos(angle) * radius,
            layout.Center.Y + MathF.Sin(angle) * radius);

        DrawMarker(canvas, marker, layout.RingWidth * 0.42f, layout.Scale,
                   SKColor.FromHsv(_hue, 100, 100));
    }

    private void DrawSquare(SKCanvas canvas, WheelLayout layout)
    {
        var pure = SKColor.FromHsv(_hue, 100, 100);

        // 横方向: 白 → その色合い
        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(layout.Square.Left, 0), new SKPoint(layout.Square.Right, 0),
                   new[] { SKColors.White, pure }, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader, IsAntialias = true })
            canvas.DrawRect(layout.Square, paint);

        // 縦方向: 透明 → 黒
        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, layout.Square.Top), new SKPoint(0, layout.Square.Bottom),
                   new[] { SKColors.Transparent, SKColors.Black }, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader, IsAntialias = true })
            canvas.DrawRect(layout.Square, paint);

        using (var edge = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 1f * layout.Scale,
            Color = new SKColor(0x00, 0x00, 0x00, 0x60), IsAntialias = true
        })
            canvas.DrawRect(layout.Square, edge);

        var marker = new SKPoint(
            layout.Square.Left + layout.Square.Width * _saturation,
            layout.Square.Top + layout.Square.Height * (1f - _value));

        DrawMarker(canvas, marker, 7f * layout.Scale, layout.Scale,
                   SKColor.FromHsv(_hue, _saturation * 100f, _value * 100f));
    }

    /// <summary>
    /// 選択位置の印。中を今の色で塗り、白と黒の輪で囲む。
    /// どんな明るさの色の上でも印が見えなくならないようにするため。
    /// </summary>
    private static void DrawMarker(SKCanvas canvas, SKPoint at, float radius, float scale, SKColor fill)
    {
        using var body = new SKPaint { Style = SKPaintStyle.Fill, Color = fill, IsAntialias = true };
        using var outer = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 3f * scale,
            Color = new SKColor(0x00, 0x00, 0x00, 0x90), IsAntialias = true
        };
        using var inner = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 2f * scale,
            Color = SKColors.White, IsAntialias = true
        };

        canvas.DrawCircle(at, radius, body);
        canvas.DrawCircle(at, radius, outer);
        canvas.DrawCircle(at, radius, inner);
    }

    // ===== 入力 =====

    private void UpdateFromPointer(System.Windows.Point p, bool starting)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var layout = Layout((int)(ActualWidth), (int)(ActualHeight));
        var point = new SKPoint((float)p.X, (float)p.Y);

        float dx = point.X - layout.Center.X;
        float dy = point.Y - layout.Center.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);

        if (starting)
        {
            // 押した場所でどちらを動かすか決め、離すまで変えない。
            // 輪から四角へはみ出しても操作が途切れないようにするため。
            bool onRing = distance > layout.OuterRadius - layout.RingWidth - Gap
                          && distance < layout.OuterRadius + layout.RingWidth;

            _drag = onRing ? DragTarget.Ring
                  : layout.Square.Contains(point) ? DragTarget.Square
                  : DragTarget.None;

            if (_drag == DragTarget.None) return;
        }

        if (_drag == DragTarget.Ring)
        {
            float angle = MathF.Atan2(dy, dx) * 180f / MathF.PI + 90f;
            Hue = angle;
        }
        else if (_drag == DragTarget.Square)
        {
            float s = (point.X - layout.Square.Left) / layout.Square.Width;
            float v = 1f - (point.Y - layout.Square.Top) / layout.Square.Height;
            SetSaturationValue(s, v);
        }
        else
        {
            return;
        }

        SelectionChanged?.Invoke();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Left) return;

        UpdateFromPointer(e.GetPosition(this), starting: true);
        if (_drag != DragTarget.None) CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag != DragTarget.None) UpdateFromPointer(e.GetPosition(this), starting: false);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag == DragTarget.None) return;

        _drag = DragTarget.None;
        ReleaseMouseCapture();
    }
}
