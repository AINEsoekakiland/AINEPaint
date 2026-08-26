using AINEPaint.Drawing;

namespace AINEPaint.History;

/// <summary>
/// 履歴1手ぶん。
///
/// Undo と Redo は「今の状態と、持っている状態を入れ替える」という
/// 同一の操作で成立する。だから Redo 用のデータを別に持つ必要がない。
/// </summary>
public interface IHistoryEntry : IDisposable
{
    string Label { get; }

    long ApproximateBytes { get; }

    void Swap(PaintDocument document);
}
