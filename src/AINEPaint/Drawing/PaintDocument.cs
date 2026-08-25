using SkiaSharp;

namespace AINEPaint.Drawing;

/// <summary>キャンバスの背景種別。</summary>
public enum CanvasBackground
{
    White,
    Transparent
}

/// <summary>
/// 1枚の作品を表すモデル。
/// 現時点ではビットマップを1枚だけ持つが、STEP 10 で
/// 「レイヤーのリスト」に置き換える前提の入れ物にしてある。
/// UI からは常にこのクラス経由で画素にアクセスすること。
/// </summary>
public sealed class PaintDocument : IDisposable
{
    /// <summary>安全側に倒した1辺の上限。メモリ量は 幅×高さ×4バイト。</summary>
    public const int MaxSide = 6000;
    public const int MinSide = 16;

    public int Width { get; }
    public int Height { get; }
    public CanvasBackground Background { get; }

    /// <summary>描画先のピクセル。STEP 7 以降、ブラシはここに書き込む。</summary>
    public SKBitmap Bitmap { get; }

    public PaintDocument(int width, int height, CanvasBackground background)
    {
        if (width < MinSide || width > MaxSide)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height < MinSide || height > MaxSide)
            throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        Background = background;

        var info = new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
        Bitmap = new SKBitmap(info);

        using var canvas = new SKCanvas(Bitmap);
        canvas.Clear(background == CanvasBackground.White ? SKColors.White : SKColors.Transparent);
    }

    /// <summary>このドキュメントが占めるおおよそのメモリ量（バイト）。</summary>
    public long ApproximateMemoryBytes => (long)Width * Height * 4;

    public void Dispose() => Bitmap.Dispose();
}
