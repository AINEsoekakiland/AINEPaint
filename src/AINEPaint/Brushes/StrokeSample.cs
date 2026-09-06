using SkiaSharp;

namespace AINEPaint.Brushes;

/// <summary>
/// ペン先の見本を描く。
///
/// 見た目を絵で用意するのではなく、実際の StrokeRenderer に描かせている。
/// そうしないと、見本と描き味がずれていくため。
/// </summary>
public static class StrokeSample
{
    /// <summary>見本1本あたりの入力点の数。多いほど滑らかだが、そのぶん時間がかかる。</summary>
    private const int Steps = 64;

    /// <summary>
    /// 横長の画像に、細い→太い→細い の1本を描いて返す。
    /// 呼び出し側が Dispose すること。
    /// </summary>
    public static SKBitmap Render(BrushKind kind, int width, int height, SKColor color,
                                  float? pressure, float? opacity, float? grain)
    {
        var info = new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);

        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(SKColors.Transparent);

        var settings = new BrushSettings
        {
            Kind = kind,
            Color = color,
            Opacity = 1f,
            // 見本どうしを比べられるよう、太さは種類によらず同じにする
            Size = height * 0.42f,
            TipPressure = pressure,
            TipOpacity = opacity,
            TipGrain = grain
        };

        using var stroke = new StrokeRenderer();

        float left = width * 0.08f;
        float right = width * 0.92f;
        float midY = height * 0.5f;
        float wave = height * 0.16f;

        stroke.Begin(bitmap, settings, PointAt(0f, left, right, midY, wave));

        for (int i = 1; i <= Steps; i++)
            stroke.AddPoint(PointAt((float)i / Steps, left, right, midY, wave), settings);

        stroke.End();
        return bitmap;
    }

    /// <summary>
    /// 見本の1点。ゆるく波打たせながら、筆圧を 0 → 1 → 0 と変える。
    /// 入り抜きの出方がペン先ごとに一番はっきり違うので、そこが見えるようにしている。
    /// </summary>
    private static StrokePoint PointAt(float t, float left, float right, float midY, float wave)
    {
        float x = left + (right - left) * t;
        float y = midY - MathF.Sin(t * MathF.PI * 1.6f) * wave;

        // 山なりの筆圧。両端をきっちり 0 にはせず、少しだけ残す
        float pressure = 0.04f + MathF.Sin(t * MathF.PI) * 0.96f;

        return new StrokePoint(x, y, pressure);
    }
}
