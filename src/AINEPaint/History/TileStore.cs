using SkiaSharp;

namespace AINEPaint.History;

/// <summary>
/// ドキュメントを固定サイズのタイルに区切って、その中身を出し入れする低レベル処理。
/// 履歴の粒度をここに集約しているので、タイルサイズの調整はこのファイルだけで済む。
/// </summary>
public static class TileStore
{
    /// <summary>
    /// 1辺のピクセル数。
    /// 小さすぎるとタイル数が増えて管理コストが上がり、
    /// 大きすぎると細い線1本でも大きな領域を保存することになる。
    /// </summary>
    public const int TileSize = 256;

    public static long BytesPerTile => (long)TileSize * TileSize * 4;

    /// <summary>指定範囲に重なるタイル座標を列挙する。</summary>
    public static IEnumerable<(int TileX, int TileY)> TilesOverlapping(SKRect rect, int docWidth, int docHeight)
    {
        int x0 = Math.Clamp((int)MathF.Floor(rect.Left), 0, Math.Max(0, docWidth - 1));
        int y0 = Math.Clamp((int)MathF.Floor(rect.Top), 0, Math.Max(0, docHeight - 1));
        int x1 = Math.Clamp((int)MathF.Ceiling(rect.Right), 0, docWidth);
        int y1 = Math.Clamp((int)MathF.Ceiling(rect.Bottom), 0, docHeight);

        if (x1 <= x0 || y1 <= y0) yield break;

        for (int ty = y0 / TileSize; ty <= (y1 - 1) / TileSize; ty++)
        for (int tx = x0 / TileSize; tx <= (x1 - 1) / TileSize; tx++)
            yield return (tx, ty);
    }

    /// <summary>タイルの画面上の範囲。端のタイルはドキュメント境界で切り詰める。</summary>
    public static SKRectI BoundsOf(int tileX, int tileY, int docWidth, int docHeight)
    {
        int left = tileX * TileSize;
        int top = tileY * TileSize;
        return new SKRectI(
            left, top,
            Math.Min(left + TileSize, docWidth),
            Math.Min(top + TileSize, docHeight));
    }

    /// <summary>タイル1枚分の中身を複製して取り出す。</summary>
    public static SKBitmap Copy(SKBitmap source, SKRectI bounds)
    {
        var info = new SKImageInfo(bounds.Width, bounds.Height,
                                   SKImageInfo.PlatformColorType, SKAlphaType.Premul);
        var tile = new SKBitmap(info);

        using var canvas = new SKCanvas(tile);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src, FilterQuality = SKFilterQuality.None };
        canvas.DrawBitmap(source, bounds, SKRect.Create(0, 0, bounds.Width, bounds.Height), paint);

        return tile;
    }

    /// <summary>取り出しておいたタイルを書き戻す。透明も含めてそのまま上書きする。</summary>
    public static void Restore(SKBitmap target, SKBitmap tile, SKRectI bounds)
    {
        using var canvas = new SKCanvas(target);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src, FilterQuality = SKFilterQuality.None };
        canvas.DrawBitmap(tile, SKRect.Create(0, 0, tile.Width, tile.Height), bounds, paint);
    }
}
