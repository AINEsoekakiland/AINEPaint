using System.Windows;
using System.Windows.Input;
using AINEPaint.Brushes;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace AINEPaint.Drawing;

/// <summary>
/// キャンバスの表示と入力を受け持つコントロール。
/// ・ドキュメントを Viewport に従って描画する
/// ・ズーム / パン操作を Viewport に伝える
/// ・ブラシ入力を StrokeRenderer に渡す
/// 画素の加工そのものは StrokeRenderer 側の責務。
/// </summary>
public class CanvasView : SKElement
{
    private PaintDocument? _document;
    private SKBitmap? _checkerTile;

    private bool _isPanning;
    private Point _lastPointerPosition;

    private readonly StrokeRenderer _stroke = new();
    private bool _isDrawing;

    public Viewport Viewport { get; } = new();

    /// <summary>下部バーの設定と共有する。UI 側が値を書き換えれば次のストロークから反映される。</summary>
    public BrushSettings Brush { get; } = new();

    /// <summary>スペースキーが押されている間 true。MainWindow から設定する。</summary>
    public bool IsPanModifierDown { get; set; }

    /// <summary>表示状態（倍率・サイズ）が変わったときに発火。ステータスバー更新用。</summary>
    public event Action? ViewStateChanged;

    /// <summary>1ストロークが完了したときに発火。STEP 9 の Undo 記録で使う。</summary>
    public event Action<SKRect>? StrokeCompleted;

    public CanvasView()
    {
        Focusable = true;
        ClipToBounds = true;
        Viewport.Changed += () =>
        {
            InvalidateVisual();
            ViewStateChanged?.Invoke();
        };
    }

    public PaintDocument? Document
    {
        get => _document;
        set
        {
            CancelStroke();
            _document = value;
            Cursor = value is null ? Cursors.Arrow : Cursors.Cross;
            FitToWindow();
            InvalidateVisual();
            ViewStateChanged?.Invoke();
        }
    }

    /// <summary>SKElement は物理ピクセルで描くので、DIP との比率を持っておく。</summary>
    private float DpiScale =>
        ActualWidth > 0 && CanvasSize.Width > 0 ? (float)(CanvasSize.Width / ActualWidth) : 1f;

    public void FitToWindow()
    {
        if (_document is null) return;
        Viewport.FitToView(_document.Width, _document.Height,
                           (float)CanvasSize.Width, (float)CanvasSize.Height);
    }

    public void ZoomByStep(float factor)
    {
        if (_document is null) return;
        Viewport.ZoomAt((float)CanvasSize.Width * 0.5f, (float)CanvasSize.Height * 0.5f, factor);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (_document is not null && info.PreviousSize.Width == 0)
            FitToWindow();
    }

    // ===== 描画 =====

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(0x14, 0x14, 0x14));

        if (_document is null)
            return;

        var matrix = Viewport.Matrix;
        var screenRect = matrix.MapRect(SKRect.Create(0, 0, _document.Width, _document.Height));

        // 背景（画素には焼き込まない）
        if (_document.Background == CanvasBackground.Transparent)
        {
            _checkerTile ??= CreateCheckerTile();
            using var shader = SKShader.CreateBitmap(_checkerTile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            using var checkerPaint = new SKPaint { Shader = shader };
            canvas.DrawRect(screenRect, checkerPaint);
        }
        else
        {
            using var whitePaint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(screenRect, whitePaint);
        }

        // ドキュメント本体
        using (var paint = new SKPaint
               {
                   FilterQuality = Viewport.Scale >= 1f ? SKFilterQuality.None : SKFilterQuality.Medium
               })
        {
            canvas.Save();
            canvas.SetMatrix(matrix);
            canvas.DrawBitmap(_document.Bitmap, 0, 0, paint);
            canvas.Restore();
        }

        using var border = new SKPaint
        {
            Color = new SKColor(0x00, 0x00, 0x00, 0x90),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = false
        };
        canvas.DrawRect(screenRect, border);
    }

    private static SKBitmap CreateCheckerTile()
    {
        const int cell = 8;
        var tile = new SKBitmap(cell * 2, cell * 2);
        using var c = new SKCanvas(tile);
        c.Clear(new SKColor(0x50, 0x50, 0x50));
        using var p = new SKPaint { Color = new SKColor(0x3A, 0x3A, 0x3A) };
        c.DrawRect(0, 0, cell, cell, p);
        c.DrawRect(cell, cell, cell, cell, p);
        return tile;
    }

    // ===== 入力 =====

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_document is null) return;

        var p = e.GetPosition(this);
        float s = DpiScale;
        Viewport.ZoomAt((float)p.X * s, (float)p.Y * s, e.Delta > 0 ? 1.12f : 1f / 1.12f);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (_document is null) return;

        bool wantsPan = e.ChangedButton == MouseButton.Middle
                        || (e.ChangedButton == MouseButton.Left && IsPanModifierDown);

        if (wantsPan)
        {
            _isPanning = true;
            _lastPointerPosition = e.GetPosition(this);
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            _isDrawing = true;
            _stroke.Begin(_document.Bitmap, Brush, ToStrokePoint(e));
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isPanning)
        {
            var current = e.GetPosition(this);
            float s = DpiScale;
            Viewport.Pan((float)(current.X - _lastPointerPosition.X) * s,
                         (float)(current.Y - _lastPointerPosition.Y) * s);
            _lastPointerPosition = current;
            return;
        }

        if (_isDrawing && e.LeftButton == MouseButtonState.Pressed)
        {
            _stroke.AddPoint(ToStrokePoint(e), Brush);
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (_isPanning)
        {
            _isPanning = false;
            Cursor = _document is null ? Cursors.Arrow : Cursors.Cross;
            ReleaseMouseCapture();
            return;
        }

        if (_isDrawing && e.ChangedButton == MouseButton.Left)
        {
            var dirty = _stroke.DirtyRect;
            _stroke.End();
            _isDrawing = false;
            ReleaseMouseCapture();
            InvalidateVisual();
            StrokeCompleted?.Invoke(dirty);
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        CancelStroke();
    }

    private void CancelStroke()
    {
        if (!_isDrawing) return;
        _stroke.End();
        _isDrawing = false;
        InvalidateVisual();
    }

    /// <summary>画面座標をドキュメント座標へ変換し、可能なら筆圧も拾う。</summary>
    private StrokePoint ToStrokePoint(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        float s = DpiScale;
        var doc = Viewport.ToDocument((float)p.X * s, (float)p.Y * s);
        return new StrokePoint(doc.X, doc.Y, GetPressure());
    }

    /// <summary>
    /// ペンタブレットが使われている場合のみ筆圧を返す。
    /// マウスや取得失敗時は 1.0（＝筆圧なし）にフォールバックする。
    /// </summary>
    private float GetPressure()
    {
        try
        {
            var device = Stylus.CurrentStylusDevice;
            if (device is null || device.TabletDevice?.Type != TabletDeviceType.Stylus)
                return 1f;

            var points = device.GetStylusPoints(this);
            if (points.Count == 0) return 1f;

            float pressure = points[points.Count - 1].PressureFactor;
            return pressure <= 0f ? 1f : Math.Clamp(pressure, 0.05f, 1f);
        }
        catch
        {
            return 1f;
        }
    }
}
