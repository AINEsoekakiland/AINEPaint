using AINEPaint.Drawing;
using SkiaSharp;

namespace AINEPaint.History;

/// <summary>
/// Undo / Redo の履歴。
///
/// ドキュメント全体を毎回複製するのではなく、
/// 「書き換わったタイルの、書き換わる直前の中身」だけを保存する。
/// 細い線を1本引いた程度なら数百KBで済む。
///
/// 上限は手数とメモリ量の両方で見る。どちらかに達したら古い履歴から捨てる。
/// 将来の設定画面から変えられるよう、値はプロパティにしてある。
/// </summary>
public sealed class HistoryStack : IDisposable
{
    private readonly List<HistoryEntry> _undo = new();
    private readonly List<HistoryEntry> _redo = new();

    public int MaxEntries { get; set; } = 50;
    public long MaxBytes { get; set; } = 512L * 1024 * 1024;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>履歴の状態が変わったときに発火。メニューの有効・無効の更新に使う。</summary>
    public event Action? Changed;

    /// <summary>
    /// これから書き換わる範囲の「今の中身」を記録する。
    /// 必ずドキュメントを変更する<em>前</em>に呼ぶこと。
    /// </summary>
    public void Capture(PaintDocument document, SKRect rect, string label)
    {
        if (rect.IsEmpty) return;

        var entry = new HistoryEntry(label);

        foreach (var (tx, ty) in TileStore.TilesOverlapping(rect, document.Width, document.Height))
        {
            var bounds = TileStore.BoundsOf(tx, ty, document.Width, document.Height);
            if (bounds.Width <= 0 || bounds.Height <= 0) continue;

            entry.AddTile(tx, ty, TileStore.Copy(document.Bitmap, bounds));
        }

        if (entry.IsEmpty)
        {
            entry.Dispose();
            return;
        }

        _undo.Add(entry);

        // 新しい操作をした時点で、やり直せる先は消える
        ClearRedo();
        Trim();

        Changed?.Invoke();
    }

    /// <summary>直前の操作を取り消す。再描画が必要な範囲を返す。</summary>
    public SKRect Undo(PaintDocument document) => Move(_undo, _redo, document);

    /// <summary>取り消した操作をやり直す。再描画が必要な範囲を返す。</summary>
    public SKRect Redo(PaintDocument document) => Move(_redo, _undo, document);

    private SKRect Move(List<HistoryEntry> from, List<HistoryEntry> to, PaintDocument document)
    {
        if (from.Count == 0) return SKRect.Empty;

        var entry = from[^1];
        from.RemoveAt(from.Count - 1);

        // 入れ替えた結果、entry には「入れ替える前の中身」が入る。
        // そのまま反対側のスタックへ積めば、逆操作としてそのまま使える。
        var affected = entry.SwapWith(document.Bitmap, document.Width, document.Height);
        to.Add(entry);

        Changed?.Invoke();
        return affected;
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
