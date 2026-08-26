using AINEPaint.Drawing;
using AINEPaint.Layers;
using SkiaSharp;

namespace AINEPaint.History;

/// <summary>あるレイヤーの、書き換わったタイルの中身を保持する履歴。</summary>
public sealed class TileHistoryEntry : IHistoryEntry
{
    private readonly List<(int TileX, int TileY, SKBitmap Pixels)> _tiles = new();
    private readonly Layer _layer;

    public TileHistoryEntry(string label, Layer layer)
    {
        Label = label;
        _layer = layer;
    }

    public string Label { get; }

    public long ApproximateBytes { get; private set; }

    public bool IsEmpty => _tiles.Count == 0;

    public void AddTile(int tileX, int tileY, SKBitmap pixels)
    {
        _tiles.Add((tileX, tileY, pixels));
        ApproximateBytes += (long)pixels.Width * pixels.Height * 4;
    }

    public void Swap(PaintDocument document)
    {
        for (int i = 0; i < _tiles.Count; i++)
        {
            var (tx, ty, stored) = _tiles[i];
            var bounds = TileStore.BoundsOf(tx, ty, document.Width, document.Height);

            var current = TileStore.Copy(_layer.Bitmap, bounds);
            TileStore.Restore(_layer.Bitmap, stored, bounds);
            stored.Dispose();

            _tiles[i] = (tx, ty, current);
        }
    }

    public void Dispose()
    {
        foreach (var (_, _, pixels) in _tiles)
            pixels.Dispose();
        _tiles.Clear();
        ApproximateBytes = 0;
    }
}
