using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace AINEPaint.Color;

/// <summary>
/// 色の成分を1つ選ぶ横スライダー。
///
/// 帯そのものが「つまみを動かした先の色」になっているので、
/// 数字を読まなくても目で選べる。赤・緑・青それぞれに1本ずつ使う。
/// </summary>
public class ChannelSlider : SKElement
{
    private byte _value;
    private SKColor _from = SKColors.Black;
    private SKColor _to = SKColors.White;
    private bool _dragging;

    public event Action? ValueChanged;

    public ChannelSlider()
    {
        Cursor = Cursors.Hand;
    }

    /// <summary>0〜255</summary>
    public byte Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            InvalidateVisual();
        }
    }

    /// <summary>帯の両端の色。成分以外の値が変わったら呼び直す。</summary>
    public void SetTrack(SKColor from, SKColor to)
    {
        _from = from;
        _to = to;
        InvalidateVisual();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float scale = ActualWidth > 0 ? (float)(e.Info.Width / ActualWidth) : 1f;

        float knobRadius = e.Info.Height * 0.5f - 2f * scale;
        float left = knobRadius;
        float right = e.Info.Width - knobRadius;
        float centerY = e.Info.Height * 0.5f;
        float trackHeight = e.Info.Height * 0.42f;

        var track = new SKRect(left, centerY - trackHeight * 0.5f, right, centerY + trackHeight * 0.5f);
        float round = trackHeight * 0.5f;

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(track.Left, 0), new SKPoint(track.Right, 0),
                   new[] { _from, _to }, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader, IsAntialias = true })
            canvas.DrawRoundRect(track, round, round, paint);

        using (var edge = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 1f * scale,
            Color = new SKColor(0x00, 0x00, 0x00, 0x60), IsAntialias = true
        })
            canvas.DrawRoundRect(track, round, round, edge);

        float x = left + (right - left) * (_value / 255f);

        using (var body = new SKPaint
        {
            Style = SKPaintStyle.Fill, IsAntialias = true,
            Color = Lerp(_from, _to, _value / 255f)
        })
            canvas.DrawCircle(x, centerY, knobRadius, body);

        using (var outer = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 3f * scale,
            Color = new SKColor(0x00, 0x00, 0x00, 0x90), IsAntialias = true
        })
            canvas.DrawCircle(x, centerY, knobRadius, outer);

        using (var inner = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 2f * scale,
            Color = SKColors.White, IsAntialias = true
        })
            canvas.DrawCircle(x, centerY, knobRadius, inner);
    }

    private static SKColor Lerp(SKColor a, SKColor b, float t)
        => new((byte)(a.Red + (b.Red - a.Red) * t),
               (byte)(a.Green + (b.Green - a.Green) * t),
               (byte)(a.Blue + (b.Blue - a.Blue) * t));

    private void UpdateFromPointer(System.Windows.Point p)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        // つまみの半径ぶん、両端は詰まっている
        double knob = ActualHeight * 0.5 - 2;
        double usable = ActualWidth - knob * 2;
        if (usable <= 0) return;

        double t = Math.Clamp((p.X - knob) / usable, 0.0, 1.0);

        Value = (byte)Math.Round(t * 255.0);
        ValueChanged?.Invoke();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Left) return;

        _dragging = true;
        CaptureMouse();
        UpdateFromPointer(e.GetPosition(this));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) UpdateFromPointer(e.GetPosition(this));
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();
    }
}
