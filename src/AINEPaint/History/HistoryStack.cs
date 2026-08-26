using AINEPaint.Drawing;
using AINEPaint.Layers;
using SkiaSharp;

namespace AINEPaint.History;

/// <summary>
/// Undo / Redo の履歴。
///
/// 画素の変更は「書き換わったタイルの、書き換わる直前の中身」だけを保存する。
/// レイヤーの追加・削除・並び替えは参照と並び順だけを保存する。
/// どちらも Undo / Redo は「入れ替え」という同じ操作で処理できる。
///
/// 上限は手数とメモリ量の両方で見る。どちらかに達したら古い履歴から捨てる。
/// 将来の設定画面から変えられるよう、値はプロパティにしてある。
/// </summary>
public sealed class HistoryStack : IDisposable
{
    private readonly List<IHistoryEntry> _undo = new();
    private readonly List<IHistoryEntry> _redo = new();

    public int MaxEntries { get; set; } = 50;
    public long MaxBytes { get; set; } = 512L * 1024 * 1024;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>履歴の状態が変わったときに発火。メニューの有効・無効の更新に使う。</summary>
    public event Action? Changed;

    /// <summary>
    /// これから書き換わる画素範囲の「今の中身」を記録する。
    /// 必ずレイヤーを変更する前に呼ぶこと。
    /// </summary>
    public void CapturePixels(PaintDocument document, Layer layer, SKRect rect, string label)
    {
        if (rect.IsEmpty) return;

        var entry = new TileHistoryEntry(label, layer);

        foreach (var (tx, ty) in TileStore.TilesOverlapping(rect, document.Width, document.Height))
        {
            var bounds = TileStore.BoundsOf(tx, ty, document.Width, document.Height);
            if (bounds.Width <= 0 || bounds.Height <= 0) continue;

            entry.AddTile(tx, ty, TileStore.Copy(layer.Bitmap, bounds));
        }

        if (entry.IsEmpty)
        {
            entry.Dispose();
            return;
        }

        Push(entry);
    }

    /// <summary>
    /// レイヤー構成を変える操作の「変更前の状態」を記録する。
    /// 必ず操作する前に呼ぶこと。
    /// </summary>
    public void CaptureStructure(PaintDocument document, string label)
    {
        var (layers, activeIndex) = document.SnapshotStructure();
        Push(new StructureHistoryEntry(label, layers, activeIndex));
    }

    private void Push(IHistoryEntry entry)
    {
        _undo.Add(entry);

        // 新しい操作をした時点で、やり直せる先は消える
        ClearRedo();
        Trim();

        Changed?.Invoke();
    }

    public void Undo(PaintDocument document) => Move(_undo, _redo, document);

    public void Redo(PaintDocument document) => Move(_redo, _undo, document);

    private void Move(List<IHistoryEntry> from, List<IHistoryEntry> to, PaintDocument document)
    {
        if (from.Count == 0) return;

        var entry = from[^1];
        from.RemoveAt(from.Count - 1);

        // 入れ替えた結果、entry には「入れ替える前の状態」が入る。
        // そのまま反対側のスタックへ積めば、逆操作としてそのまま使える。
        entry.Swap(document);
        to.Add(entry);

        Changed?.Invoke();
    }

    /// <summary>キャンバスを作り直したときなど、履歴が意味を失う場面で呼ぶ。</summary>
    public void Clear()
    {
        foreach (var entry in _undo) entry.Dispose();
        _undo.Clear();
        ClearRedo();
        Changed?.Invoke();
    }

    private void ClearRedo()
    {
        foreach (var entry in _redo) entry.Dispose();
        _redo.Clear();
    }

    private void Trim()
    {
        while (_undo.Count > MaxEntries)
        {
            _undo[0].Dispose();
            _undo.RemoveAt(0);
        }

        while (_undo.Count > 1 && TotalBytes() > MaxBytes)
        {
            _undo[0].Dispose();
            _undo.RemoveAt(0);
        }
    }

    private long TotalBytes()
    {
        long total = 0;
        foreach (var entry in _undo) total += entry.ApproximateBytes;
        foreach (var entry in _redo) total += entry.ApproximateBytes;
        return total;
    }

    public void Dispose() => Clear();
}
