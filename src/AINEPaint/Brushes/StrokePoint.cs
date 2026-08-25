namespace AINEPaint.Brushes;

/// <summary>
/// ストロークを構成する1点。座標は「ドキュメント座標」。
/// Pressure は 0.0〜1.0。マウス入力では常に 1.0 が入る。
/// ペンタブレット対応を後から入れても、この型より上は変更不要にしてある。
/// </summary>
public readonly record struct StrokePoint(float X, float Y, float Pressure)
{
    public static StrokePoint FromMouse(float x, float y) => new(x, y, 1f);
}
