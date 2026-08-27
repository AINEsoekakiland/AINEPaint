using SkiaSharp;

namespace AINEPaint.Selection;

/// <summary>
/// 変形中の「浮いた選択部分」。
///
/// 元のレイヤーは確定するまで一切書き換えない。
/// 元の位置に開いている穴は、表示のたびに動的に開けている。
/// こうすることで
/// ・Undo 履歴は確定時の1手だけで済む
/// ・キャンセルは捨てるだけで成立する
/// という利点がある。
///
/// 移動・拡大縮小・回転はすべて選択範囲の中心を基準にする。
/// 掴んだ場所によって基準が変わる方式より、予測しやすいため。
/// </summary>
public sealed class FloatingSelection : IDisposable
{
    private FloatingSelection(SKBitmap pixels, SKRectI pixelRect, SKPath sourcePath)
    {
        Pixels = pixels;
        PixelRect = pixelRect;
        SourcePath = sourcePath;
    }

    public SKBitmap Pixels { get; }

    /// <summary>切り出した元の位置と大きさ（ドキュメント座標）。</summary>
    public SKRectI PixelRect { get; }

    /// <summary>元の選択の形。表示・確定時にここを消して穴を開ける。</summary>
    public SKPath SourcePath { get; }

    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float Scale { get; set; } = 1f;
    public float Rotation { get; set; }

    public SKPoint OriginalCenter => new(PixelRect.MidX, PixelRect.MidY);

    public SKPoint Center => new(PixelRect.MidX + OffsetX, PixelRect.MidY + OffsetY);

    public SKMatrix Matrix
    {
        get
        {
            float cx = PixelRect.MidX;
            float cy = PixelRect.MidY;

            var m = SKMatrix.CreateTranslation(-cx, -cy);
            m = m.PostConcat(SKMatrix.CreateScale(Scale, Scale));
            m = m.PostConcat(SKMatrix.CreateRotationDegrees(Rotation));
            m = m.PostConcat(SKMatrix.CreateTranslation(cx + OffsetX, cy + OffsetY));
            return m;
        }
    }

    /// <summary>変形後の四隅（ドキュメント座標）。左上・右上・右下・左下の順。</summary>
    public SKPoint[] Corners
    {
        get
        {
            var m = Matrix;
            return new[]
            {
                m.MapPoint(PixelRect.Left,  PixelRect.Top),
                m.MapPoint(PixelRect.Right, PixelRect.Top),
                m.MapPoint(PixelRect.Right, PixelRect.Bottom),
                m.MapPoint(PixelRect.Left,  PixelRect.Bottom)
            };
        }
    }

    public SKRect DestinationBounds => Matrix.MapRect(SKRect.Create(
        PixelRect.Left, PixelRect.Top, PixelRect.Width, PixelRect.Height));

    /// <summary>確定・表示のどちらでも書き換わり得る範囲。Undo 履歴はこれを記録する。</summary>
    public SKRect AffectedBounds
    {
        get
        {
            var union = SKRect.Union(SourcePath.Bounds, DestinationBounds);
            union.Inflate(2f, 2f);
            return union;
        }
    }

    /// <summary>変形後の選択の形。確定後はこれが新しい選択範囲になる。</summary>
    public SKPath CreateTransformedPath()
    {
        var path = new SKPath(SourcePath);
        path.Transform(Matrix);
        return path;
    }

    /// <summary>選択範囲の画素をレイヤーから切り出す。切り出せなければ null。</summary>
    public static FloatingSelection? Lift(SKBitmap layer, SKPath selection)
    {
        var rect = SKRectI.Round(selection.Bounds);
        rect.Intersect(new SKRectI(0, 0, layer.Width, layer.Height));

        if (rect.Width <= 0 || rect.Height <= 0) return null;

        var info = new SKImageInfo(rect.Width, rect.Height,
                                   SKImageInfo.PlatformColorType, SKAlphaType.Premul);
        var pixels = new SKBitmap(info);

        using (var canvas = new SKCanvas(pixels))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(-rect.Left, -rect.Top);
            canvas.ClipPath(selection, SKClipOperation.Intersect, antialias: true);
            canvas.DrawBitmap(layer, 0, 0);
        }

        return new FloatingSelection(pixels, rect, new SKPath(selection));
    }

    /// <summary>
    /// ドキュメント座標のキャンバスへ「穴を開けて、変形後を描く」。
    /// 表示にも確定にも同じ処理を使うので、見た目と結果が必ず一致する。
    /// </summary>
    public void DrawInto(SKCanvas canvas)
    {
        using (var clear = new SKPaint { BlendMode = SKBlendMode.Clear, IsAntialias = true })
            canvas.DrawPath(SourcePath, clear);

        canvas.Save();

        // SkiaSharp の Concat は ref 引数を取るので、いったん変数に受ける
        var matrix = Matrix;
        canvas.Concat(ref matrix);

        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
            canvas.DrawBitmap(Pixels, PixelRect.Left, PixelRect.Top, paint);

        canvas.Restore();
    }

    public void Dispose()
    {
        Pixels.Dispose();
        SourcePath.Dispose();
    }
}
