using SkiaSharp;

namespace AINEPaint.Selection;

/// <summary>
/// 選択範囲。ドキュメント座標のパスとして持つ。
///
/// ビットマップのマスクではなくパスで持っているのは、
/// ・拡大表示しても選択の縁がぼけない
/// ・Skia の ClipPath にそのまま渡せる（描画制限が1行で済む）
/// ・STEP 12b の変形で、選択そのものを行列で動かせる
/// という3点のため。
/// </summary>
public sealed class SelectionRegion
{
    private SKPath? _path;

    /// <summary>選択が変わったときに発火。</summary>
    public event Action? Changed;

    public bool IsActive => _path is { IsEmpty: false };

    /// <summary>描画を制限するためのパス。選択が無ければ null。</summary>
    public SKPath? Path => IsActive ? _path : null;

    public SKRect Bounds => _path?.Bounds ?? SKRect.Empty;

    public void SetRectangle(SKRect rect)
    {
        var normalized = SKRect.Create(
            Math.Min(rect.Left, rect.Right),
            Math.Min(rect.Top, rect.Bottom),
            Math.Abs(rect.Width),
            Math.Abs(rect.Height));

        if (normalized.Width < 1f || normalized.Height < 1f)
        {
            Clear();
            return;
        }

        var path = new SKPath();
        path.AddRect(normalized);
        Replace(path);
    }

    public void SetPath(SKPath path)
    {
        var copy = new SKPath(path);
        copy.Close();

        if (copy.Bounds.Width < 1f || copy.Bounds.Height < 1f)
        {
            copy.Dispose();
            Clear();
            return;
        }

        Replace(copy);
    }

    public void SelectAll(int width, int height)
        => SetRectangle(SKRect.Create(0, 0, width, height));

    public void Clear()
    {
        if (_path is null) return;

        _path.Dispose();
        _path = null;
        Changed?.Invoke();
    }

    private void Replace(SKPath path)
    {
        _path?.Dispose();
        _path = path;
        Changed?.Invoke();
    }

    /// <summary>選択の縁を画面へ描く。</summary>
    public void DrawOutline(SKCanvas canvas, SKMatrix viewMatrix, float antsPhase)
    {
        if (_path is null) return;

        using var screenPath = new SKPath(_path);
        screenPath.Transform(viewMatrix);

        DrawMarchingAnts(canvas, screenPath, antsPhase);
    }

    /// <summary>
    /// 黒の実線の上に白の破線を重ね、破線の位相をずらし続けることで
    /// 選択の縁が流れて見えるようにする（マーチングアンツ）。
    /// 明るい絵の上でも暗い絵の上でも見えるよう、必ず二重線で描く。
    /// </summary>
    public static void DrawMarchingAnts(SKCanvas canvas, SKPath screenPath, float antsPhase)
    {
        using var dark = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            Color = SKColors.Black,
            IsAntialias = false
        };
        canvas.DrawPath(screenPath, dark);

        using var light = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            Color = SKColors.White,
            IsAntialias = false,
            PathEffect = SKPathEffect.CreateDash(new[] { 5f, 5f }, antsPhase)
        };
        canvas.DrawPath(screenPath, light);
    }
}
