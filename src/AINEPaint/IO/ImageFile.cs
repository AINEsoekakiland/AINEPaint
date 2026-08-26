using System.IO;
using AINEPaint.Drawing;
using AINEPaint.Layers;
using SkiaSharp;

namespace AINEPaint.IO;

/// <summary>PNG / JPEG の読み込みと、PNG への書き出し。</summary>
public static class ImageFile
{
    public const string OpenFilter =
        "画像ファイル (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg";

    public const string PngFilter = "PNG 画像 (*.png)|*.png";

    /// <summary>
    /// 全レイヤーを合成して PNG に書き出す。
    /// 背景が「透明」のキャンバスは透明のまま保存する。
    /// </summary>
    public static void ExportPng(PaintDocument document, string path)
    {
        var info = new SKImageInfo(document.Width, document.Height,
                                   SKColorType.Rgba8888, SKAlphaType.Premul);

        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("書き出し用の領域を確保できませんでした。");

        var canvas = surface.Canvas;
        canvas.Clear(document.Background == CanvasBackground.White
            ? SKColors.White
            : SKColors.Transparent);

        document.Render(canvas);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG に変換できませんでした。");

        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    /// <summary>画像を1枚のレイヤーとして読み込み、新しいドキュメントを作る。</summary>
    public static PaintDocument Import(string path)
    {
        using var bitmap = SKBitmap.Decode(path)
            ?? throw new InvalidDataException("画像として読み取れませんでした。");

        if (bitmap.Width < PaintDocument.MinSide || bitmap.Height < PaintDocument.MinSide)
            throw new InvalidDataException($"画像が小さすぎます（{PaintDocument.MinSide}px 以上が必要です）。");

        if (bitmap.Width > PaintDocument.MaxSide || bitmap.Height > PaintDocument.MaxSide)
            throw new InvalidDataException(
                $"画像が大きすぎます（{bitmap.Width}×{bitmap.Height}）。" +
                $"1辺 {PaintDocument.MaxSide}px までに対応しています。");

        string name = Path.GetFileNameWithoutExtension(path);
        var layer = Layer.FromBitmap(bitmap, bitmap.Width, bitmap.Height,
                                     string.IsNullOrWhiteSpace(name) ? "レイヤー 1" : name);

        // JPEG は不透明、PNG は透明を含み得る。どちらも画素側が持っているので
        // キャンバス背景は透明にしておけば見た目は変わらない。
        return PaintDocument.FromLayers(bitmap.Width, bitmap.Height,
                                        CanvasBackground.Transparent, new[] { layer }, 0);
    }
}
