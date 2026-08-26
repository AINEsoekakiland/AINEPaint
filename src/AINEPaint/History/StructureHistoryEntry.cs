using AINEPaint.Drawing;
using AINEPaint.Layers;

namespace AINEPaint.History;

/// <summary>
/// レイヤーの追加・削除・複製・並び替えを取り消すための履歴。
/// 保持するのはレイヤーの参照と並び順だけなので、画素の複製は発生しない。
///
/// 注意: 削除されたレイヤーはこの履歴からしか参照されなくなるが、
/// ここでは Dispose しない。ドキュメントに戻される可能性があるため。
/// 履歴から捨てられた時点で参照が切れ、SkiaSharp 側で解放される。
/// </summary>
public sealed class StructureHistoryEntry : IHistoryEntry
{
    private List<Layer> _layers;
    private int _activeIndex;

    public StructureHistoryEntry(string label, List<Layer> layers, int activeIndex)
    {
        Label = label;
        _layers = layers;
        _activeIndex = activeIndex;
    }

    public string Label { get; }

    /// <summary>参照だけなので、履歴のメモリ上限には算入しない。</summary>
    public long ApproximateBytes => 0;

    public void Swap(PaintDocument document)
    {
        var (currentLayers, currentActive) = document.SnapshotStructure();
        document.RestoreStructure(_layers, _activeIndex);
        _layers = currentLayers;
        _activeIndex = currentActive;
    }

    public void Dispose()
    {
        _layers.Clear();
    }
}
