using SkiaSharp;

namespace AINEPaint.Brushes;

/// <summary>
/// ブラシの種類。
/// 設定ファイルには名前で書き出しているので、途中に足しても古い設定は壊れない。
/// Pencil / Eraser は左のツールボタンと1対1。それ以外は「ペン先」として選ぶ。
/// </summary>
public enum BrushKind
{
    /// <summary>なめらか。標準のペン。</summary>
    Pen,

    /// <summary>Gペン。筆圧の効きが硬く、入り抜きが鋭い。線画向け。</summary>
    GPen,

    /// <summary>マーカー。筆圧を無視して太さ一定、少し透ける。</summary>
    Marker,

    /// <summary>毛筆。筆圧に加えて、速く動かすほど細くなる。</summary>
    Brush,

    /// <summary>エアブラシ。縁がぼけ、重ねるほど濃くなる。</summary>
    Airbrush,

    /// <summary>クレヨン。ざらついた質感。</summary>
    Crayon,

    Pencil,
    Eraser
}

/// <summary>
/// ブラシの設定値。UI（下部バー）とレンダラの間を繋ぐ唯一の受け渡し口。
/// プリセット保存（Phase 2）はこのクラスを直列化すれば済むようにしておく。
/// </summary>
public sealed class BrushSettings
{
    public BrushKind Kind { get; set; } = BrushKind.Pen;

    /// <summary>直径（ドキュメントピクセル）。</summary>
    public float Size { get; set; } = 12f;

    /// <summary>0.0〜1.0。</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>
    /// 筆圧を線の太さに反映するか。false ならペンタブでも太さは一定。
    /// マウスでは元から筆圧が来ないので、この値に関わらず一定。
    /// </summary>
    public bool UsePressure { get; set; } = true;

    public SKColor Color { get; set; } = SKColors.Black;

    /// <summary>塗りつぶしの色の許容差（0〜255）。大きいほど広い範囲が塗られる。</summary>
    public int FillTolerance { get; set; } = 24;

    /// <summary>塗りつぶした範囲を外へ広げる量（ピクセル）。線の縁の隙間を埋める。</summary>
    public int FillExpand { get; set; } = 1;

    // ---- ペン先の調整値。null はそのペン先の既定値を使うという意味 ----

    /// <summary>筆圧の効き（0〜1）。0 で太さ一定。</summary>
    public float? TipPressure { get; set; }

    /// <summary>濃さ（0〜1）。ペン先そのものの透け具合。</summary>
    public float? TipOpacity { get; set; }

    /// <summary>ざらつき（0〜1）。</summary>
    public float? TipGrain { get; set; }

    /// <summary>ペン先（Pencil / Eraser 以外）かどうか。</summary>
    public static bool IsPenTip(BrushKind kind)
        => kind is not (BrushKind.Pencil or BrushKind.Eraser);
}
