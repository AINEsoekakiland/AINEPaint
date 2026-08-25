using System.Windows;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace AINEPaint.Drawing;

/// <summary>
/// キャンバスの表示コントロール。
/// 責務は「ドキュメントを Viewport に従って描画する」ことと
/// 「ズーム / パン操作を Viewport に伝える」ことの2つだけ。
/// ブラシ描画は STEP 7 でここに乗せる。
/// </summary>
public class CanvasView : SKElement
{
    private PaintDocument? _document;
    private SKBitmap? _checkerTile;
    private bool _isPanning;
    private Point _lastPointerPosition;

    public Viewport Viewport { get; } = new();

    /// <summary>スペースキーが押されている間 true。MainWindow から設定する。</summary>
    public bool IsPanModifierDown { get; set; }

    /// <summary>表示状態（倍率・サイズ）が変わったときに発火。ステータスバー更新用。</summary>
    public event Action? ViewStateChanged;

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
            _document = value;
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

    /// <summary>ビューの中心を基準にズームする。メニューのズームイン/アウト用。</summary>
    public void ZoomByStep(float factor)
    {
        if (_document is null) return;
        Viewport.ZoomAt((float)CanvasSize.Width * 0.5f, (float)CanvasSize.Height * 0.5f, factor);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        // ウィンドウサイズ変更でキャンバスが画面外に飛ばないよう、初回だけ収める
        if (_document is not null && info.PreviousSize.Width == 0)
            FitToWindow();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(0x14, 0x14, 0x14));

        if (_document is null)
            return;

        var matrix = Viewport.Matrix;
        var docRect = SKRect.Create(0, 0, _document.Width, _document.Height);
        var screenRect = matrix.MapRect(docRect);

        // 透明背景なら市松模様を敷く（キャンバス範囲内のみ）
        if (_document.Background == CanvasBackground.Transparent)
        {
            _checkerTile ??= CreateCheckerTile();
            using var shader = SKShader.CreateBitmap(_checkerTile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            using var checkerPaint = new SKPaint { Shader = shader };
            canvas.DrawRect(screenRect, checkerPaint);
        }

        // ドキュメント本体
        using (var paint = new SKPaint
               {
                   // 拡大時はピクセルをくっきり、縮小時は滑らかに
                   FilterQuality = Viewport.Scale >= 1f ? SKFilterQuality.None : SKFilterQuality.Medium
               })
        {
            canvas.Save();
            canvas.SetMatrix(matrix);
            canvas.DrawBitmap(_document.Bitmap, 0, 0, paint);
            canvas.Restore();
        }

        // 用紙の輪郭
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
        var light = new SKColor(0x50, 0x50, 0x50);
        var dark = new SKColor(0x3A, 0x3A, 0x3A);
        c.Clear(light);
        using var p = new SKPaint { Color = dark };
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
        float factor = e.Delta > 0 ? 1.12f : 1f / 1.12f;
        Viewport.ZoomAt((float)p.X * s, (float)p.Y * s, factor);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        bool wantsPan = e.ChangedButton == MouseButton.Middle
                        || (e.ChangedButton == MouseButton.Left && IsPanModifierDown);

        if (_document is not null && wantsPan)
        {
            _isPanning = true;
            _lastPointerPosition = e.GetPosition(this);
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isPanning) return;

        var p = e.GetPosition(this);
        float s = DpiScale;
        Viewport.Pan((float)(p.X - _lastPointerPosition.X) * s,
                     (float)(p.Y - _lastPointerPosition.Y) * s);
        _lastPointerPosition = p;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_isPanning) return;

        _isPanning = false;
        Cursor = Cursors.Arrow;
        ReleaseMouseCapture();
    }
}
