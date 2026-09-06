using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AINEPaint.Brushes;
using AINEPaint.Layers;
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

    /// <summary>いまポインタがある位置（物理ピクセル）。キャンバス外なら null。</summary>
    private SKPoint? _cursorScreen;

    private SKPath? _selectionPreview;
    private SKPoint _selectionStart;
    private bool _isSelecting;

    private FloatingSelection? _floating;
    private TransformGrip _grip = TransformGrip.None;
    private SKPoint _gripStartDoc;
    private float _gripStartScale;
    private float _gripStartRotation;
    private float _gripStartOffsetX;
    private float _gripStartOffsetY;
    private float _gripStartDistance;
    private float _gripStartAngle;

    /// <summary>選択の縁の破線を流すためのタイマー。選択が無いときは止めておく。</summary>
    private readonly DispatcherTimer _antsTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };

    // ---- 長押しスポイト ----

    /// <summary>この時間だけ押したまま動かさなければ、スポイトに切り替わる。</summary>
    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>これ以上動かしたら「描くつもり」と見なして長押しを取り消す（画面ピクセル）。</summary>
    private const double LongPressSlop = 6.0;

    private readonly DispatcherTimer _longPressTimer = new() { Interval = LongPressDelay };
    private Point _pressOrigin;
    private bool _tempEyedropper;
    private SKTypeface? _labelTypeface;
    private float _antsPhase;

    public Viewport Viewport { get; } = new();

    /// <summary>下部バーの設定と共有する。UI 側が値を書き換えれば次のストロークから反映される。</summary>
    public BrushSettings Brush { get; } = new();

    /// <summary>スペースキーが押されている間 true。MainWindow から設定する。</summary>
    public bool IsPanModifierDown { get; set; }

    /// <summary>移動ツールが選択されている間 true。左ドラッグがパンになる。</summary>
    public bool PanToolActive { get; set; }

    /// <summary>スポイトが選択されている間 true。左クリックで色を拾う。</summary>
    public bool EyedropperActive
    {
        get => _eyedropperActive;
        set
        {
            if (_eyedropperActive == value) return;
            _eyedropperActive = value;
            UpdateCursor();
            InvalidateVisual();
        }
    }

    private bool _eyedropperActive;

    /// <summary>スポイトの表示を出すべきか。ツールとして選んでいる間と、長押し中。</summary>
    private bool EyedropperShowing => EyedropperActive || _tempEyedropper;

    /// <summary>
    /// ブラシの太さを表す丸カーソルを出すかどうか。
    /// ペン / 鉛筆 / 消しゴムのときだけ true にする（初期ツールはペン）。
    /// </summary>
    public bool BrushCursorVisible
    {
        get => _brushCursorVisible;
        set
        {
            if (_brushCursorVisible == value) return;
            _brushCursorVisible = value;
            UpdateCursor();
            InvalidateVisual();
        }
    }

    private bool _brushCursorVisible = true;

    /// <summary>
    /// ポインタの形を決める。
    /// 丸カーソルを出している間は標準カーソルを消す。
    /// OS が描く矢印と、こちらが1フレーム遅れて描く丸が並ぶと、遅れが目立つため。
    /// </summary>
    private void UpdateCursor()
    {
        if (_document is null)
        {
            Cursor = Cursors.Arrow;
            return;
        }

        Cursor = BrushCursorVisible && !EyedropperActive ? Cursors.None : Cursors.Cross;
    }

    /// <summary>塗りつぶしが選択されている間 true。左クリックで塗る。</summary>
    public bool FillToolActive { get; set; }

    /// <summary>選択ツールの種類。None のときは選択操作をしない。</summary>
    public SelectionTool SelectionMode { get; set; } = SelectionTool.None;

    /// <summary>現在の選択範囲。</summary>
    public SelectionRegion Selection { get; } = new();

    /// <summary>変形ツールが選択されている間 true。</summary>
    public bool TransformToolActive { get; set; }

    /// <summary>変形中かどうか。</summary>
    public bool IsTransforming => _floating is not null;

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

        _longPressTimer.Tick += (_, _) => BeginTemporaryEyedropper();

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

            UpdateCursor();
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
            _document.Render(canvas, preview, previewPaint, quality, Selection.Path, _floating);
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

        if (_floating is not null)
            DrawTransformHandles(canvas, matrix);

        DrawPointerOverlay(canvas);
    }

    // ===== ポインタ表示（太さの丸 / スポイトの拡大鏡） =====

    /// <summary>ポインタの位置に補助表示を出す。すべて画面座標で描く。</summary>
    private void DrawPointerOverlay(SKCanvas canvas)
    {
        if (_document is null || _isPanning) return;
        if (_cursorScreen is not { } screen) return;

        if (EyedropperShowing)
        {
            DrawEyedropperLoupe(canvas, screen);
            return;
        }

        if (BrushCursorVisible && _floating is null)
            DrawBrushCursor(canvas, screen);
    }

    /// <summary>いまのブラシの太さと同じ大きさの丸を出す。太さが一目で分かるようにするため。</summary>
    private void DrawBrushCursor(SKCanvas canvas, SKPoint screen)
    {
        float radius = Brush.Size * 0.5f * Viewport.Scale;

        // 小さすぎると見えなくなるので下限を設ける
        if (radius < 2f) radius = 2f;

        using var shadow = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f,
            Color = new SKColor(0x00, 0x00, 0x00, 0x80),
            IsAntialias = true
        };
        using var line = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f,
            Color = new SKColor(0xFF, 0xFF, 0xFF, 0xE0),
            IsAntialias = true
        };

        canvas.DrawCircle(screen.X, screen.Y, radius, shadow);
        canvas.DrawCircle(screen.X, screen.Y, radius, line);

        // 標準カーソルを消しているので、中心が分かる目印を出す
        using var center = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(0xFF, 0xFF, 0xFF, 0xC0),
            IsAntialias = true
        };
        using var centerEdge = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = new SKColor(0x00, 0x00, 0x00, 0x80),
            IsAntialias = true
        };
        canvas.DrawCircle(screen.X, screen.Y, 1.6f, center);
        canvas.DrawCircle(screen.X, screen.Y, 1.6f, centerEdge);
    }

    private const float LoupeRadius = 46f;
    private const float LoupeZoom = 10f;
    private const float LoupeGap = 22f;

    /// <summary>
    /// スポイト用の拡大表示。カーソルの上に丸を出し、その中身を拡大して見せる。
    /// 中心の1マスがこれから拾う画素で、外側の輪がその色。
    /// </summary>
    private void DrawEyedropperLoupe(SKCanvas canvas, SKPoint screen)
    {
        if (_document is null) return;

        float dpi = DpiScale;
        float radius = LoupeRadius * dpi;
        float zoom = LoupeZoom * dpi;

        var doc = Viewport.ToDocument(screen.X, screen.Y);
        int px = (int)MathF.Floor(doc.X);
        int py = (int)MathF.Floor(doc.Y);

        bool inside = px >= 0 && py >= 0 && px < _document.Width && py < _document.Height;
        if (!inside) return;

        // 拾う画素がちょうど真ん中に来るよう、カーソルの位置に重ねて出す
        float cx = screen.X;
        float cy = screen.Y;

        var sample = _document.SamplePixel(px, py);

        using var circle = new SKPath();
        circle.AddCircle(cx, cy, radius);

        canvas.Save();
        canvas.ClipPath(circle, SKClipOperation.Intersect, true);

        // キャンバスの外側は暗いまま見せる
        using (var back = new SKPaint { Color = new SKColor(0x14, 0x14, 0x14) })
            canvas.DrawRect(cx - radius, cy - radius, radius * 2f, radius * 2f, back);

        var zoomMatrix = SKMatrix.CreateScaleTranslation(
            zoom, zoom, cx - doc.X * zoom, cy - doc.Y * zoom);

        var docRect = SKRect.Create(0, 0, _document.Width, _document.Height);
        var docOnScreen = zoomMatrix.MapRect(docRect);

        // 市松模様は拡大せず、画面上の大きさのまま出す
        if (_document.Background == CanvasBackground.Transparent)
        {
            _checkerTile ??= CreateCheckerTile();
            using var shader = SKShader.CreateBitmap(_checkerTile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
            using var checkerPaint = new SKPaint { Shader = shader };
            canvas.DrawRect(docOnScreen, checkerPaint);
        }
        else
        {
            using var white = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(docOnScreen, white);
        }

        canvas.Save();
        canvas.SetMatrix(zoomMatrix);
        _document.Render(canvas, null, null, SKFilterQuality.None, null, null);
        canvas.Restore();

        // これから拾う画素を1マスだけ囲う
        float bx = (px - doc.X) * zoom + cx;
        float by = (py - doc.Y) * zoom + cy;
        var cell = new SKRect(bx, by, bx + zoom, by + zoom);

        using (var cellShadow = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 3f,
            Color = new SKColor(0x00, 0x00, 0x00, 0x90), IsAntialias = false
        })
            canvas.DrawRect(cell, cellShadow);

        using (var cellLine = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f,
            Color = SKColors.White, IsAntialias = false
        })
            canvas.DrawRect(cell, cellLine);

        canvas.Restore();

        // 外周の輪が、いまカーソルの下にある色
        float ringWidth = 6f * dpi;

        using (var ring = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = ringWidth,
            Color = sample.Alpha == 0 ? new SKColor(0x60, 0x60, 0x60) : new SKColor(sample.Red, sample.Green, sample.Blue),
            IsAntialias = true
        })
            canvas.DrawCircle(cx, cy, radius - ringWidth * 0.5f, ring);

        using (var edge = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f,
            Color = new SKColor(0x00, 0x00, 0x00, 0xA0), IsAntialias = true
        })
        {
            canvas.DrawCircle(cx, cy, radius, edge);
            canvas.DrawCircle(cx, cy, radius - ringWidth, edge);
        }

        DrawSourceLayerLabel(canvas, cx, cy - radius - LoupeGap * 0.5f * dpi, px, py, dpi);
    }

    /// <summary>
    /// 拡大鏡の上に、その色を置いているレイヤーの名前を出す。
    /// 別のレイヤーの色を拾ってしまう間違いに、その場で気づけるようにするため。
    /// </summary>
    private void DrawSourceLayerLabel(SKCanvas canvas, float cx, float bottom, int px, int py, float dpi)
    {
        if (_document is null) return;

        Layer? source = _document.SampleTopLayer(px, py);
        string text = source?.Name
                      ?? (_document.Background == CanvasBackground.White ? "背景" : "透明");

        // レイヤー名に日本語が入るので、確実に出せる書体を選んでおく
        _labelTypeface ??= SKFontManager.Default.MatchCharacter("ja", 'あ') ?? SKTypeface.Default;

        using var textPaint = new SKPaint
        {
            Color = source is null ? new SKColor(0xB0, 0xB0, 0xB0) : SKColors.White,
            TextSize = 12f * dpi,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            Typeface = _labelTypeface
        };

        float padX = 8f * dpi;
        float padY = 4f * dpi;
        float width = textPaint.MeasureText(text) + padX * 2f;
        float height = textPaint.TextSize + padY * 2f;

        var box = new SKRect(cx - width * 0.5f, bottom - height, cx + width * 0.5f, bottom);

        // 画面の上端に収まらないときは拡大鏡の下へ回す
        if (box.Top < 0)
        {
            float shift = (LoupeRadius * 2f + LoupeGap) * dpi + height;
            box = new SKRect(box.Left, box.Top + shift, box.Right, box.Bottom + shift);
        }

        using (var back = new SKPaint { Color = new SKColor(0x20, 0x20, 0x24, 0xE0), IsAntialias = true })
            canvas.DrawRoundRect(box, 4f * dpi, 4f * dpi, back);

        using (var edge = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 1f,
            Color = new SKColor(0x00, 0x00, 0x00, 0x80), IsAntialias = true
        })
            canvas.DrawRoundRect(box, 4f * dpi, 4f * dpi, edge);

        canvas.DrawText(text, cx, box.Bottom - padY - textPaint.FontMetrics.Descent, textPaint);
    }

    /// <summary>変形中の枠とハンドル。すべて画面座標で描くので、拡大しても大きさが変わらない。</summary>
    private void DrawTransformHandles(SKCanvas canvas, SKMatrix viewMatrix)
    {
        if (_floating is null) return;

        var corners = _floating.Corners;
        var screen = new SKPoint[corners.Length];
        for (int i = 0; i < corners.Length; i++)
            screen[i] = viewMatrix.MapPoint(corners[i]);

        using var frame = new SKPath();
        frame.MoveTo(screen[0]);
        for (int i = 1; i < screen.Length; i++) frame.LineTo(screen[i]);
        frame.Close();

        using var line = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            Color = new SKColor(0x4E, 0xA1, 0xFF),
            IsAntialias = true
        };
        canvas.DrawPath(frame, line);

        // 回転ハンドルは上辺の中央から少し離した位置に置く
        var rotationHandle = RotationHandleScreenPoint(viewMatrix);
        canvas.DrawLine((screen[0].X + screen[1].X) / 2f, (screen[0].Y + screen[1].Y) / 2f,
                        rotationHandle.X, rotationHandle.Y, line);

        using var fill = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var edge = new SKPaint
        {
            Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f,
            Color = new SKColor(0x4E, 0xA1, 0xFF), IsAntialias = true
        };

        foreach (var p in screen)
        {
            var box = new SKRect(p.X - HandleRadius, p.Y - HandleRadius,
                                 p.X + HandleRadius, p.Y + HandleRadius);
            canvas.DrawRect(box, fill);
            canvas.DrawRect(box, edge);
        }

        canvas.DrawCircle(rotationHandle, HandleRadius, fill);
        canvas.DrawCircle(rotationHandle, HandleRadius, edge);
    }

    private const float HandleRadius = 6f;
    private const float RotationHandleDistance = 28f;

    private SKPoint RotationHandleScreenPoint(SKMatrix viewMatrix)
    {
        var corners = _floating!.Corners;
        var topLeft = viewMatrix.MapPoint(corners[0]);
        var topRight = viewMatrix.MapPoint(corners[1]);
        var center = viewMatrix.MapPoint(_floating.Center);

        var mid = new SKPoint((topLeft.X + topRight.X) / 2f, (topLeft.Y + topRight.Y) / 2f);

        float dx = mid.X - center.X;
        float dy = mid.Y - center.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 0.001f) return new SKPoint(mid.X, mid.Y - RotationHandleDistance);

        return new SKPoint(mid.X + dx / length * RotationHandleDistance,
                           mid.Y + dy / length * RotationHandleDistance);
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

        if (e.ChangedButton == MouseButton.Left && _floating is not null)
        {
            var screen = e.GetPosition(this);
            float dpi = DpiScale;
            var grip = HitTestGrip(new SKPoint((float)screen.X * dpi, (float)screen.Y * dpi));

            if (grip != TransformGrip.None)
            {
                BeginGrip(grip, DocumentPointOf(e));
                CaptureMouse();
                e.Handled = true;
                return;
            }

            // 枠の外を押したら確定する
            CommitTransform();
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

            // 押したまま動かさなければスポイトに変わる。動かしたら普通に描く。
            _pressOrigin = e.GetPosition(this);
            _longPressTimer.Start();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        UpdatePointerPosition(e);

        if (_isPanning)
        {
            var current = e.GetPosition(this);
            float s = DpiScale;
            Viewport.Pan((float)(current.X - _lastPointerPosition.X) * s,
                         (float)(current.Y - _lastPointerPosition.Y) * s);
            _lastPointerPosition = current;
            return;
        }

        if (_grip != TransformGrip.None && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateGrip(DocumentPointOf(e));
            return;
        }

        if (_isSelecting && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateSelection(e);
            return;
        }

        if (_tempEyedropper)
        {
            InvalidateVisual();   // 拡大鏡がカーソルに追随する
            return;
        }

        if (_longPressTimer.IsEnabled)
        {
            var now = e.GetPosition(this);
            if (Math.Abs(now.X - _pressOrigin.X) > LongPressSlop ||
                Math.Abs(now.Y - _pressOrigin.Y) > LongPressSlop)
                _longPressTimer.Stop();
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
            UpdateCursor();
            ReleaseMouseCapture();
            return;
        }

        if (_grip != TransformGrip.None && e.ChangedButton == MouseButton.Left)
        {
            _grip = TransformGrip.None;
            ReleaseMouseCapture();
            return;
        }

        if (_isSelecting && e.ChangedButton == MouseButton.Left)
        {
            CommitSelection();
            ReleaseMouseCapture();
            return;
        }

        if (e.ChangedButton == MouseButton.Left && _tempEyedropper)
        {
            PickColorAt(e);
            EndTemporaryEyedropper();
            ReleaseMouseCapture();
            return;
        }

        _longPressTimer.Stop();

        if (_isDrawing && e.ChangedButton == MouseButton.Left)
        {
            FinishStroke();
            ReleaseMouseCapture();
        }
    }

    /// <summary>補助表示のためにポインタ位置を覚える。表示が出ているときだけ描き直す。</summary>
    private void UpdatePointerPosition(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        float s = DpiScale;
        _cursorScreen = new SKPoint((float)p.X * s, (float)p.Y * s);

        if (_document is not null && (EyedropperActive || BrushCursorVisible) && !_isPanning)
            InvalidateVisual();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdatePointerPosition(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (_cursorScreen is null) return;
        _cursorScreen = null;
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);

        _longPressTimer.Stop();
        EndTemporaryEyedropper();

        // 何らかの理由でキャプチャが外れた場合も、描いた分は捨てずに確定させる
        FinishStroke();
    }

    /// <summary>
    /// 長押しで一時的にスポイトへ切り替える。
    /// すでに置き始めていた点は捨てる。履歴はストロークを離した時にしか積まないので、
    /// ここで捨てても「元に戻す」には何も残らない。
    /// </summary>
    private void BeginTemporaryEyedropper()
    {
        _longPressTimer.Stop();

        if (!_isDrawing || _tempEyedropper) return;

        _isDrawing = false;
        _stroke.Cancel();
        _tempEyedropper = true;
        InvalidateVisual();
    }

    private void EndTemporaryEyedropper()
    {
        if (!_tempEyedropper) return;

        _tempEyedropper = false;
        InvalidateVisual();
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

    // ===== 変形 =====

    /// <summary>選択部分を切り出して浮かせる。元のレイヤーはまだ書き換えない。</summary>
    public void BeginTransform()
    {
        if (_floating is not null) return;
        if (_document?.ActiveLayer is not { IsVisible: true } layer) return;
        if (Selection.Path is not { } path) return;

        _floating = FloatingSelection.Lift(layer.Bitmap, path);
        InvalidateVisual();
    }

    /// <summary>変形を確定してレイヤーへ書き込む。</summary>
    public void CommitTransform()
    {
        if (_floating is null || _document?.ActiveLayer is not { } layer)
        {
            CancelTransform();
            return;
        }

        var affected = _floating.AffectedBounds;
        BeforeDocumentChange?.Invoke(affected);

        // 表示に使っているのと同じ処理で書き込むので、見た目と結果が必ず一致する
        using (var canvas = new SKCanvas(layer.Bitmap))
            _floating.DrawInto(canvas);

        using (var moved = _floating.CreateTransformedPath())
            Selection.SetPath(moved);

        _floating.Dispose();
        _floating = null;
        _grip = TransformGrip.None;

        InvalidateVisual();
        StrokeCompleted?.Invoke(affected);
    }

    /// <summary>変形を捨てる。元のレイヤーは触っていないので、捨てるだけで元に戻る。</summary>
    public void CancelTransform()
    {
        if (_floating is null) return;

        _floating.Dispose();
        _floating = null;
        _grip = TransformGrip.None;

        InvalidateVisual();
    }

    private TransformGrip HitTestGrip(SKPoint screenPoint)
    {
        if (_floating is null) return TransformGrip.None;

        var view = Viewport.Matrix;
        float threshold = HandleRadius + 4f;

        var rotation = RotationHandleScreenPoint(view);
        if (Distance(screenPoint, rotation) <= threshold) return TransformGrip.Rotate;

        foreach (var corner in _floating.Corners)
            if (Distance(screenPoint, view.MapPoint(corner)) <= threshold)
                return TransformGrip.Scale;

        // 枠の内側なら移動
        var corners = _floating.Corners;
        using var frame = new SKPath();
        frame.MoveTo(view.MapPoint(corners[0]));
        for (int i = 1; i < corners.Length; i++) frame.LineTo(view.MapPoint(corners[i]));
        frame.Close();

        return frame.Contains(screenPoint.X, screenPoint.Y) ? TransformGrip.Move : TransformGrip.None;
    }

    private static float Distance(SKPoint a, SKPoint b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private void BeginGrip(TransformGrip grip, SKPoint documentPoint)
    {
        if (_floating is null) return;

        _grip = grip;
        _gripStartDoc = documentPoint;
        _gripStartScale = _floating.Scale;
        _gripStartRotation = _floating.Rotation;
        _gripStartOffsetX = _floating.OffsetX;
        _gripStartOffsetY = _floating.OffsetY;

        var center = _floating.Center;
        _gripStartDistance = Math.Max(1f, Distance(documentPoint, center));
        _gripStartAngle = MathF.Atan2(documentPoint.Y - center.Y, documentPoint.X - center.X);
    }

    private void UpdateGrip(SKPoint documentPoint)
    {
        if (_floating is null || _grip == TransformGrip.None) return;

        switch (_grip)
        {
            case TransformGrip.Move:
                _floating.OffsetX = _gripStartOffsetX + (documentPoint.X - _gripStartDoc.X);
                _floating.OffsetY = _gripStartOffsetY + (documentPoint.Y - _gripStartDoc.Y);
                break;

            case TransformGrip.Scale:
            {
                var center = _floating.Center;
                float distance = Distance(documentPoint, center);
                float scale = _gripStartScale * (distance / _gripStartDistance);
                _floating.Scale = Math.Clamp(scale, 0.05f, 20f);
                break;
            }

            case TransformGrip.Rotate:
            {
                var center = _floating.Center;
                float angle = MathF.Atan2(documentPoint.Y - center.Y, documentPoint.X - center.X);
                float degrees = (angle - _gripStartAngle) * 180f / MathF.PI;
                _floating.Rotation = _gripStartRotation + degrees;
                break;
            }
        }

        InvalidateVisual();
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
            if (!Brush.UsePressure) return 1f;

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
