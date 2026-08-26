using SkiaSharp;

namespace AINEPaint.History;

/// <summary>
/// 履歴1手ぶん。「変更されたタイルの、変更前の中身」を持つ。
///
/// Undo も Redo も「今の中身と、持っている中身を入れ替える」という
/// 同一の操作で実現できる。だから Redo 用に別のデータを持つ必要がない。
/// </summary>
public sealed class HistoryEntry : IDisposable
{
    private readonly List<(int TileX, int TileY, SKBitmap Pixels)> _tiles = new();

    public HistoryEntry(string label)
    {
        Label = label;
    }

    public string Label { get; }

    public long ApproximateBytes { get; private set; }

    public bool IsEmpty => _tiles.Count == 0;

    public void AddTile(int tileX, int tileY, SKBitmap pixels)
    {
        _tiles.Add((tileX, tileY, pixels));
        ApproximateBytes += (long)pixels.Width * pixels.Height * 4;
    }

    /// <summary>ドキュメントの該当タイルと、保持している中身を入れ替える。</summary>
    public SKRect SwapWith(SKBitmap document, int docWidth, int docHeight)
    {
        var affected = SKRect.Empty;

        for (int i = 0; i < _tiles.Count; i++)
        {
            var (tx, ty, stored) = _tiles[i];
            var bounds = TileStore.BoundsOf(tx, ty, docWidth, docHeight);

            var current = TileStore.Copy(document, bounds);
            TileStore.Restore(document, stored, bounds);
            stored.Dispose();

            _tiles[i] = (tx, ty, current);

            var rect = new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
            affected = affected.IsEmpty ? rect : SKRect.Union(affected, rect);
        }

        return affected;
    }

    public void Dispose()
    {
        foreach (var (_, _, pixels) in _tiles)
            pixels.Dispose();
        _tiles.Clear();
        ApproximateBytes = 0;
    }
}
