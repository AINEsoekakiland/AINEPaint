using System.Runtime.InteropServices;
using SkiaSharp;

namespace AINEPaint.Drawing;

/// <summary>
/// 塗りつぶし。
///
/// 計算（どこを塗るか）と適用（実際に塗る）を分けてある。
/// Undo 履歴は「書き換わる範囲の変更前の中身」を必要とするが、
/// その範囲は塗ってみるまで分からないため、
/// 先に範囲だけ求めて履歴に記録し、そのあとで適用する。
///
/// 画素の書き込みは自前で行わず、塗る形をマスク画像にして Skia に描かせている。
/// ポインタ操作を書かずに済み、アンチエイリアスの縁も自然に馴染む。
/// </summary>
public static class FloodFill
{
    /// <summary>塗りつぶす形。使い終わったら Dispose すること。</summary>
    public sealed class FillMask : IDisposable
    {
        public FillMask(SKImage image, SKRectI bounds)
        {
            Image = image;
            Bounds = bounds;
        }

        public SKImage Image { get; }
        public SKRectI Bounds { get; }

        public void Dispose() => Image.Dispose();
    }

    /// <summary>
    /// 塗る範囲を求める。塗る場所が無ければ null。
    /// </summary>
    /// <param name="tolerance">色の許容差（0〜255）。大きいほど広がる。</param>
    /// <param name="expand">求めた範囲を何ピクセル広げるか。線のアンチエイリアス部分の隙間を埋める。</param>
    public static FillMask? Compute(SKBitmap source, int startX, int startY, int tolerance, int expand)
    {
        int width = source.Width;
        int height = source.Height;

        if (startX < 0 || startY < 0 || startX >= width || startY >= height)
            return null;

        var bytes = source.GetPixelSpan();
        if (bytes.Length == 0) return null;

        int stride = source.RowBytes / 4;
        var pixels = MemoryMarshal.Cast<byte, uint>(bytes);

        uint targetColor = pixels[startY * stride + startX];

        var mask = new byte[width * height];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((startX, startY));

        int minX = startX, maxX = startX, minY = startY, maxY = startY;

        // 走査線方式。1点ずつ積むより積む回数が減り、大きな面でも詰まりにくい。
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (mask[y * width + x] != 0) continue;

            int left = x;
            while (left > 0 && mask[y * width + left - 1] == 0 &&
                   IsSimilar(pixels[y * stride + left - 1], targetColor, tolerance))
                left--;

            int right = x;
            while (right < width - 1 && mask[y * width + right + 1] == 0 &&
                   IsSimilar(pixels[y * stride + right + 1], targetColor, tolerance))
                right++;

            if (!IsSimilar(pixels[y * stride + x], targetColor, tolerance)) continue;

            for (int i = left; i <= right; i++)
                mask[y * width + i] = 255;

            if (left < minX) minX = left;
            if (right > maxX) maxX = right;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;

            PushRow(stack, pixels, mask, width, stride, left, right, y - 1, targetColor, tolerance);
            PushRow(stack, pixels, mask, width, stride, left, right, y + 1, targetColor, tolerance);
        }

        if (expand > 0)
        {
            Dilate(mask, width, height, expand);
            minX = Math.Max(0, minX - expand);
            minY = Math.Max(0, minY - expand);
            maxX = Math.Min(width - 1, maxX + expand);
            maxY = Math.Min(height - 1, maxY + expand);
        }

        var info = new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Unpremul);
        var image = SKImage.FromPixelCopy(info, mask);
        if (image is null) return null;

        return new FillMask(image, new SKRectI(minX, minY, maxX + 1, maxY + 1));
    }

    /// <summary>求めた形を実際に塗る。</summary>
    public static void Apply(SKBitmap target, FillMask mask, SKColor color, SKPath? clip = null)
    {
        using var canvas = new SKCanvas(target);
        using var paint = new SKPaint { Color = color, IsAntialias = false };

        canvas.Save();
        if (clip is not null)
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
        canvas.DrawImage(mask.Image, 0, 0, paint);
        canvas.Restore();
    }

    private static void PushRow(Stack<(int, int)> stack, ReadOnlySpan<uint> pixels, byte[] mask,
                                int width, int stride, int left, int right, int y,
                                uint targetColor, int tolerance)
    {
        if (y < 0) return;
        if (y * width >= mask.Length) return;

        bool inRun = false;
        for (int x = left; x <= right; x++)
        {
            bool match = mask[y * width + x] == 0 &&
                         IsSimilar(pixels[y * stride + x], targetColor, tolerance);

            if (match && !inRun)
            {
                stack.Push((x, y));
                inRun = true;
            }
            else if (!match)
            {
                inRun = false;
            }
        }
    }

    /// <summary>塗った範囲を外側へ広げる。線のアンチエイリアス部分に白い隙間が残るのを防ぐ。</summary>
    private static void Dilate(byte[] mask, int width, int height, int radius)
    {
        var source = (byte[])mask.Clone();

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (source[y * width + x] != 0) continue;

            bool near = false;
            for (int dy = -radius; dy <= radius && !near; dy++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= height) continue;

                for (int dx = -radius; dx <= radius; dx++)
                {
                    int nx = x + dx;
                    if (nx < 0 || nx >= width) continue;

                    if (source[ny * width + nx] != 0) { near = true; break; }
                }
            }

            if (near) mask[y * width + x] = 255;
        }
    }

    /// <summary>前乗算済みの画素同士をチャンネルごとに比べる。</summary>
    private static bool IsSimilar(uint a, uint b, int tolerance)
    {
        if (a == b) return true;

        int d0 = Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF));
        int d1 = Math.Abs((int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF));
        int d2 = Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF));
        int d3 = Math.Abs((int)((a >> 24) & 0xFF) - (int)((b >> 24) & 0xFF));

        return d0 <= tolerance && d1 <= tolerance && d2 <= tolerance && d3 <= tolerance;
    }
}
