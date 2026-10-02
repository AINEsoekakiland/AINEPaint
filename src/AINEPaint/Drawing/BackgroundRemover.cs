using SkiaSharp;

namespace AINEPaint.Drawing;

/// <summary>
/// 背景を消す。
///
/// 四隅の色を「背景の色」とみなし、そこから繋がっている同じような色を透明にする。
/// 四隅から辿るので、絵の内側にある同じ色（たとえば白いシャツ）は消えない。
///
/// 写真の複雑な背景は相手にできない。白地のスキャン、単色の背景紙、
/// 塗りつぶした背景の上に描いた絵——そういう素材のための機能。
/// </summary>
public static class BackgroundRemover
{
    /// <summary>四隅の色を測る正方形の一辺。1画素だけ見るとゴミを拾うので少し広く取る。</summary>
    private const int CornerPatch = 5;

    /// <summary>
    /// 消しきらずに半分だけ薄くする幅。許容値に対する倍率。
    /// 線の縁のぼかし部分を、いきなり切り落とさず滑らかに抜くために使う。
    /// </summary>
    private const float FeatherScale = 1.8f;

    /// <summary>
    /// 背景を透明にする。書き換えた範囲を返す。何も消さなかったときは null。
    /// </summary>
    /// <param name="tolerance">色の違いをどこまで同じ背景とみなすか（0〜128 くらい）。</param>
    public static SKRectI? Remove(SKBitmap bitmap, int tolerance)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        if (width <= 0 || height <= 0) return null;

        var seeds = CornerColors(bitmap);
        if (seeds.Count == 0) return null;

        float near = Math.Max(1, tolerance);
        float far = near * FeatherScale;

        var pixels = bitmap.Pixels;
        var visited = new bool[width * height];
        var queue = new Queue<int>();

        // 四隅から出発する。すでに透明な角も通り道としては使えるので入れておく
        foreach (int corner in new[] { 0, width - 1, (height - 1) * width, height * width - 1 })
        {
            if (visited[corner]) continue;
            if (Distance(pixels[corner], seeds) > far && pixels[corner].Alpha != 0) continue;

            visited[corner] = true;
            queue.Enqueue(corner);
        }

        if (queue.Count == 0) return null;

        int minX = width, minY = height, maxX = -1, maxY = -1;
        bool changed = false;

        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int x = index % width;
            int y = index / width;

            var pixel = pixels[index];
            float distance = Distance(pixel, seeds);

            if (pixel.Alpha != 0)
            {
                byte alpha;

                if (distance <= near)
                    alpha = 0;
                else
                    alpha = (byte)Math.Clamp(
                        pixel.Alpha * ((distance - near) / (far - near)), 0f, 255f);

                if (alpha != pixel.Alpha)
                {
                    // 完全な透明でも色の成分は残しておく。あとで不透明度を戻したときに色が濁らないようにするため
                    pixels[index] = new SKColor(pixel.Red, pixel.Green, pixel.Blue, alpha);
                    changed = true;

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            // ここより外側はもう背景ではないので、先へは進まない
            if (distance > far) continue;

            if (x > 0) Push(index - 1);
            if (x < width - 1) Push(index + 1);
            if (y > 0) Push(index - width);
            if (y < height - 1) Push(index + width);
        }

        if (!changed) return null;

        bitmap.Pixels = pixels;
        return new SKRectI(minX, minY, maxX + 1, maxY + 1);

        void Push(int next)
        {
            if (visited[next]) return;
            visited[next] = true;
            queue.Enqueue(next);
        }
    }

    /// <summary>四隅それぞれの平均色。似たものはまとめる。</summary>
    private static List<SKColor> CornerColors(SKBitmap bitmap)
    {
        var corners = new List<SKColor>(4);

        AddPatch(0, 0);
        AddPatch(bitmap.Width - CornerPatch, 0);
        AddPatch(0, bitmap.Height - CornerPatch);
        AddPatch(bitmap.Width - CornerPatch, bitmap.Height - CornerPatch);

        return corners;

        void AddPatch(int startX, int startY)
        {
            long r = 0, g = 0, b = 0, a = 0;
            int count = 0;

            for (int y = Math.Max(0, startY); y < Math.Min(bitmap.Height, startY + CornerPatch); y++)
            for (int x = Math.Max(0, startX); x < Math.Min(bitmap.Width, startX + CornerPatch); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                r += pixel.Red; g += pixel.Green; b += pixel.Blue; a += pixel.Alpha;
                count++;
            }

            if (count == 0) return;

            var average = new SKColor((byte)(r / count), (byte)(g / count), (byte)(b / count), (byte)(a / count));

            // 透明な角は「色」としては使えない
            if (average.Alpha < 8) return;

            foreach (var existing in corners)
                if (Difference(existing, average) < 12f) return;

            corners.Add(average);
        }
    }

    /// <summary>一番近い背景色との隔たり。</summary>
    private static float Distance(SKColor pixel, List<SKColor> seeds)
    {
        if (pixel.Alpha == 0) return 0f;

        float best = float.MaxValue;
        foreach (var seed in seeds)
        {
            float d = Difference(pixel, seed);
            if (d < best) best = d;
        }

        return best;
    }

    /// <summary>
    /// 2色の隔たり。人の目は緑に敏感で青に鈍いので、その重みを掛けている。
    /// 単純な差だと、青っぽい背景が実際より「違う色」に見えてしまう。
    /// </summary>
    private static float Difference(SKColor a, SKColor b)
    {
        float dr = a.Red - b.Red;
        float dg = a.Green - b.Green;
        float db = a.Blue - b.Blue;

        return MathF.Sqrt(dr * dr * 0.30f + dg * dg * 0.59f + db * db * 0.11f);
    }
}
