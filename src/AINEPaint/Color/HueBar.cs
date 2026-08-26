using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace AINEPaint.Color;

/// <summary>色相を選ぶ横帯。</summary>
public class HueBar : SKElement
{
    private float _hue;
    private bool _dragging;

    public event Action? SelectionChanged;

    public HueBar()
    {
        Cursor = Cursors.Hand;
    }

    /// <summary>0〜360</summary>
    public float Hue
    {
        get => _hue;
        set { _hue = Math.Clamp(value, 0f, 360f); InvalidateVisual(); }
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        var info = e.Info;
        var rect = SKRect.Create(0, 0, info.Width, info.Height);

        var colors = new SKColor[7];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = SKColor.FromHsv(i * 60f, 100, 100);

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0), new SKPoint(info.Width, 0), colors, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader })
            canvas.DrawRect(rect, paint);

        float x = _hue / 360f * info.Width;
        using var outer = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 3,
            Color = SKColors.Black.WithAlpha(160), IsAntialias = true
        };
        using var inner = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f,
            Color = SKColors.White, IsAntialias = true
        };
        canvas.DrawLine(x, 0, x, info.Height, outer);
        canvas.DrawLine(x, 0, x, info.Height, inner);
    }

    private void UpdateFromPointer(System.Windows.Point p)
    {
        if (ActualWidth <= 0) return;
        Hue = (float)(p.X / ActualWidth) * 360f;
        SelectionChanged?.Invoke();
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
