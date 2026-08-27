using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AINEPaint.Brushes;
using AINEPaint.Selection;
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

    private SKPath? _selectionPreview;
    private SKPoint _selectionStart;
    private bool _isSelecting;

    /// <summary>選択の縁の破線を流すためのタイマー。選択が無いときは止めておく。</summary>
    private readonly DispatcherTimer _antsTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private float _antsPhase;

    public Viewport Viewport { get; } = new();

    /// <summary>下部バーの設定と共有する。UI 側が値を書き換えれば次のストロークから反映される。</summary>
    public BrushSettings Brush { get; } = new();

    /// <summary>スペースキーが押されている間 true。MainWindow から設定する。</summary>
    public bool IsPanModifierDown { get; set; }

    /// <summary>移動ツールが選択されている間 true。左ドラッグがパンになる。</summary>
    public bool PanToolActive { get; set; }

    /// <summary>スポイトが選択されている間 true。左クリックで色を拾う。</summary>
    public bool EyedropperActive { get; set; }

    /// <summary>塗りつぶしが選択されている間 true。左クリックで塗る。</summary>
    public bool FillToolActive { get; set; }

    /// <summary>選択ツールの種類。None のときは選択操作をしない。</summary>
    public SelectionTool SelectionMode { get; set; } = SelectionTool.None;

    /// <summary>現在の選択範囲。</summary>
    public SelectionRegion Selection { get; } = new();

    /// <summary>表示状態（倍率・サイズ）が変わったときに発火。ステータスバー更新用。</summary>
    public event Action? ViewStateChanged;

    /// <summary>1ストロークが完了したときに発火。STEP 9 の Undo 記録で使う。</summary>
    public event Action<SKRect>? StrokeCompleted;

    /// <summary>スポイトで色を拾ったときに発火。</summary>
    public event Action<SKColor>? ColorPicked;

    /// <summary>
    /// ドキュメントの画素を書き換える直前に、書き換わる範囲を通知する。
    /// Undo 履歴は「変更前の中身」を必要とするので、必ずこの時点で記録する。
    /// </summary>
    public event Action<SKRect>? BeforeDocumentChange;

    public CanvasView()
    {
        Focusable = true;
        ClipToBounds = true;
        Viewport.Changed += () =>
        {
            InvalidateVisual();
            ViewStateChanged?.Invoke();
        };

        Selection.Changed += () =>
        {
            InvalidateVisual();
            UpdateAntsTimer();
        };

        _antsTimer.Tick += (_, _) =>
        {
            _antsPhase = (_antsPhase + 1.5f) % 10f;
            InvalidateVisual();
        };
    }

    public PaintDocument? Document
    {
        get => _document;
        set
        {
            FinishStroke();

            if (_document is not null)
            {
                _document.ContentChanged -= InvalidateVisual;
                _document.StructureChanged -= InvalidateVisual;
            }

            _document = value;

            if (_document is not null)
            {
                _document.ContentChanged += InvalidateVisual;
                _document.StructureChanged += InvalidateVisual;
            }

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

        // 全レイヤーの合成。描画中のストロークは選択中レイヤーの中で重ねる
        // （PaintDocument.Render が SaveLayer でグループ化しているので、
        //  消しゴムが下のレイヤーや背景まで削ってしまうことはない）
        {
            var preview = _stroke.PreviewBuffer;
            using SKPaint? previewPaint = preview is null ? null : _stroke.CreatePreviewPaint();

            var quality = Viewport.Scale >= 1f ? SKFilterQuality.None : SKFilterQuality.Medium;

            canvas.Save();
            canvas.SetMatrix(matrix);
            _document.Render(canvas, preview, previewPaint, quality, Selection.Path);
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

        // 選択範囲の縁は画面座標で描く（拡大しても線の太さを保つため）
        Selection.DrawOutline(canvas, matrix, _antsPhase);

        if (_selectionPreview is not null)
        {
            using var preview = new SKPath(_selectionPreview);
            preview.Transform(matrix);
            SelectionRegion.DrawMarchingAnts(canvas, preview, _antsPhase);
        }
    }

    /// <summary>選択がある間だけ破線を動かす。無いときに回し続けても無駄なので止める。</summary>
    private void UpdateAntsTimer()
    {
        bool wanted = Selection.IsActive || _isSelecting;

        if (wanted && !_antsTimer.IsEnabled) _antsTimer.Start();
        else if (!wanted && _antsTimer.IsEnabled) _antsTimer.Stop();
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
                        || (e.ChangedButton == MouseButton.Left && (IsPanModifierDown || PanToolActive));

        if (wantsPan)
        {
            _isPanning = true;
            _lastPointerPosition = e.GetPosition(this);
            Cursor = Cursors.SizeAll;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && EyedropperActive)
        {
            PickColorAt(e);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && FillToolActive)
        {
            FillAt(e);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && SelectionMode != SelectionTool.None)
        {
            BeginSelection(e);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            // 非表示のレイヤーには描かない（描いても見えず、事故のもとになる）
            if (_document.ActiveLayer is not { IsVisible: true } target) return;

            _isDrawing = true;
            _stroke.Begin(target.Bitmap, Brush, ToStrokePoint(e), Selection.Path);
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

        if (_isSelecting && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateSelection(e);
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

        if (_isSelecting && e.ChangedButton == MouseButton.Left)
        {
            CommitSelection();
            ReleaseMouseCapture();
            return;
        }

        if (_isDrawing && e.ChangedButton == MouseButton.Left)
        {
            FinishStroke();
            ReleaseMouseCapture();
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // 何らかの理由でキャプチャが外れた場合も、描いた分は捨てずに確定させる
        FinishStroke();
    }

    private void FinishStroke()
    {
        if (!_isDrawing) return;
        _isDrawing = false;

        // 合成前に、これから書き換わる範囲を通知する
        var pending = _stroke.DirtyRect;
        if (!pending.IsEmpty)
            BeforeDocumentChange?.Invoke(pending);

        var dirty = _stroke.End();
        InvalidateVisual();
        StrokeCompleted?.Invoke(dirty);
    }

    // ===== 選択範囲 =====

    private SKPoint DocumentPointOf(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        float s = DpiScale;
        return Viewport.ToDocument((float)p.X * s, (float)p.Y * s);
    }

    private void BeginSelection(MouseEventArgs e)
    {
        if (_document is null) return;

        _isSelecting = true;
        _selectionStart = DocumentPointOf(e);

        _selectionPreview?.Dispose();
        _selectionPreview = new SKPath();

        if (SelectionMode == SelectionTool.Lasso)
            _selectionPreview.MoveTo(_selectionStart);

        CaptureMouse();
        UpdateAntsTimer();
        InvalidateVisual();
    }

    private void UpdateSelection(MouseEventArgs e)
    {
        if (_selectionPreview is null) return;

        var point = DocumentPointOf(e);

        if (SelectionMode == SelectionTool.Rectangle)
        {
            _selectionPreview.Reset();
            _selectionPreview.AddRect(SKRect.Create(
                Math.Min(_selectionStart.X, point.X),
                Math.Min(_selectionStart.Y, point.Y),
                Math.Abs(point.X - _selectionStart.X),
                Math.Abs(point.Y - _selectionStart.Y)));
        }
        else
        {
            _selectionPreview.LineTo(point);
        }

        InvalidateVisual();
    }

    private void CommitSelection()
    {
        _isSelecting = false;

        if (_selectionPreview is not null)
        {
            if (_selectionPreview.Bounds.Width < 1f || _selectionPreview.Bounds.Height < 1f)
                Selection.Clear();   // ただのクリックは選択解除として扱う
            else if (SelectionMode == SelectionTool.Rectangle)
                Selection.SetRectangle(_selectionPreview.Bounds);
            else
                Selection.SetPath(_selectionPreview);

            _selectionPreview.Dispose();
            _selectionPreview = null;
        }

        UpdateAntsTimer();
        InvalidateVisual();
    }

    /// <summary>
    /// クリック位置から塗りつぶす。
    /// 塗る範囲を先に求めてから履歴に記録し、そのあとで適用する。
    /// </summary>
    private void FillAt(MouseEventArgs e)
    {
        if (_document is null) return;
        if (_document.ActiveLayer is not { IsVisible: true } target) return;

        var p = e.GetPosition(this);
        float s = DpiScale;
        var doc = Viewport.ToDocument((float)p.X * s, (float)p.Y * s);

        int x = (int)MathF.Floor(doc.X);
        int y = (int)MathF.Floor(doc.Y);
        if (x < 0 || y < 0 || x >= _document.Width || y >= _document.Height) return;

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            using var mask = FloodFill.Compute(target.Bitmap, x, y, Brush.FillTolerance, Brush.FillExpand);
            if (mask is null) return;

            var bounds = new SKRect(mask.Bounds.Left, mask.Bounds.Top, mask.Bounds.Right, mask.Bounds.Bottom);
            BeforeDocumentChange?.Invoke(bounds);

            FloodFill.Apply(target.Bitmap, mask, Brush.Color, Selection.Path);

            InvalidateVisual();
            StrokeCompleted?.Invoke(bounds);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    /// <summary>
    /// クリック位置の色を拾う。
    /// 画素が透明な場合は、その下に見えている背景（白キャンバスなら白）を返す。
    /// 透明背景のキャンバスでは市松模様は色ではないので何もしない。
    /// </summary>
    private void PickColorAt(MouseEventArgs e)
    {
        if (_document is null) return;

        var p = e.GetPosition(this);
        float s = DpiScale;
        var doc = Viewport.ToDocument((float)p.X * s, (float)p.Y * s);

        int x = (int)MathF.Floor(doc.X);
        int y = (int)MathF.Floor(doc.Y);
        if (x < 0 || y < 0 || x >= _document.Width || y >= _document.Height) return;

        var color = _document.SamplePixel(x, y);
        if (color.Alpha == 0) return;

        ColorPicked?.Invoke(new SKColor(color.Red, color.Green, color.Blue));
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
