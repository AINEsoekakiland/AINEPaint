using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace AINEPaint.Color;

/// <summary>
/// 彩度（横）と明度（縦）を選ぶ四角。色相は外から与える。
/// カラーホイールを追加する場合も、この部品を差し替えるだけで済む。
/// </summary>
public class SaturationValueBox : SKElement
{
    private float _hue;
    private float _saturation = 1f;
    private float _value = 1f;
    private bool _dragging;

    public event Action? SelectionChanged;

    public SaturationValueBox()
    {
        Cursor = Cursors.Cross;
    }

    /// <summary>0〜360</summary>
    public float Hue
    {
        get => _hue;
        set { _hue = value; InvalidateVisual(); }
    }

    /// <summary>0〜1</summary>
    public float Saturation => _saturation;

    /// <summary>0〜1</summary>
    public float Value => _value;

    public void SetSaturationValue(float saturation, float value, bool notify = false)
    {
        _saturation = Math.Clamp(saturation, 0f, 1f);
        _value = Math.Clamp(value, 0f, 1f);
        InvalidateVisual();
        if (notify) SelectionChanged?.Invoke();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        var info = e.Info;
        var rect = SKRect.Create(0, 0, info.Width, info.Height);

        // 1. 純色で塗る
        using (var basePaint = new SKPaint { Color = SKColor.FromHsv(_hue, 100, 100) })
            canvas.DrawRect(rect, basePaint);

        // 2. 左から白のグラデーション（彩度）
        using (var satShader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0), new SKPoint(info.Width, 0),
                   new[] { SKColors.White, SKColors.White.WithAlpha(0) },
                   SKShaderTileMode.Clamp))
        using (var satPaint = new SKPaint { Shader = satShader })
            canvas.DrawRect(rect, satPaint);

        // 3. 下へ黒のグラデーション（明度）
        using (var valShader = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0), new SKPoint(0, info.Height),
                   new[] { SKColors.Black.WithAlpha(0), SKColors.Black },
                   SKShaderTileMode.Clamp))
        using (var valPaint = new SKPaint { Shader = valShader })
            canvas.DrawRect(rect, valPaint);

        // 選択位置のマーカー
        float cx = _saturation * info.Width;
        float cy = (1f - _value) * info.Height;

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
        canvas.DrawCircle(cx, cy, 7, outer);
        canvas.DrawCircle(cx, cy, 7, inner);
    }

    private void UpdateFromPointer(System.Windows.Point p)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        SetSaturationValue((float)(p.X / ActualWidth), 1f - (float)(p.Y / ActualHeight), notify: true);
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
