using SkiaSharp;

namespace AINEPaint.Brushes;

/// <summary>
/// ペン先ごとの味付け。
///
/// 既定値はここに1行ずつ並べてある。利用者が画面から変えられるのは
/// 「筆圧の効き」「濃さ」「ざらつき」の3つだけで、残りはペン先の個性として固定する。
/// 全部を触れるようにすると、どのペン先も同じものになってしまうため。
/// </summary>
public readonly record struct TipProfile(
    float PressureExponent,
    float PressureFloor,
    bool UseSpeed,
    float SpacingRatio,
    float BlurRatio,
    byte StampAlpha,
    float Grain,
    float OpacityScale)
{
    public static TipProfile For(BrushKind kind) => kind switch
    {
        // 硬く、入り抜きが鋭い。線画向け
        BrushKind.GPen =>
            new TipProfile(2.2f, 0.04f, false, 0.08f, 0f, 255, 0f, 1f),

        // 筆圧を無視して太さ一定。少し透ける
        BrushKind.Marker =>
            new TipProfile(1f, 1f, false, 0.12f, 0f, 255, 0f, 0.8f),

        // 筆圧に加えて、速さでも細る
        BrushKind.Brush =>
            new TipProfile(1.1f, 0.02f, true, 0.08f, 0f, 255, 0f, 1f),

        // 縁がぼけ、重ねるほど濃くなる
        BrushKind.Airbrush =>
            new TipProfile(1f, 0.55f, false, 0.22f, 0.30f, 26, 0f, 1f),

        // ざらついた質感
        BrushKind.Crayon =>
            new TipProfile(1.3f, 0.15f, false, 0.22f, 0f, 210, 0.75f, 0.95f),

        // 少し薄く、わずかにざらつく
        BrushKind.Pencil =>
            new TipProfile(1.4f, 0.05f, false, 0.10f, 0f, 255, 0.25f, 0.75f),

        // ペンと消しゴムは同じ挙動（消しゴムは合成のしかただけが違う）
        _ => new TipProfile(1.4f, 0f, false, 0.10f, 0f, 255, 0f, 1f)
    };

    /// <summary>
    /// 「筆圧の効き」を 0〜1 で表した値。
    /// 0 なら筆圧を完全に無視、1 なら弱い筆圧で限界まで細くなる。
    /// 内部の PressureFloor は「筆圧が0でも残る太さの割合」なので、裏返しの関係になる。
    /// </summary>
    public float PressureAmount => 1f - PressureFloor;

    /// <summary>画面で変えられる3つの値を差し替えた profile を返す。null の項目は既定のまま。</summary>
    public TipProfile With(float? pressureAmount, float? opacityScale, float? grain) => this with
    {
        PressureFloor = pressureAmount is { } a ? 1f - Math.Clamp(a, 0f, 1f) : PressureFloor,
        OpacityScale = opacityScale is { } o ? Math.Clamp(o, 0.05f, 1f) : OpacityScale,
        Grain = grain is { } g ? Math.Clamp(g, 0f, 1f) : Grain
    };

    /// <summary>ブラシ設定に入っている調整値を反映した profile を作る。</summary>
    public static TipProfile Resolve(BrushSettings settings)
        => For(settings.Kind).With(settings.TipPressure, settings.TipOpacity, settings.TipGrain);
}
