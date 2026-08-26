using System.ComponentModel;
using System.Runtime.CompilerServices;
using SkiaSharp;

namespace AINEPaint.Layers;

/// <summary>
/// 1枚のレイヤー。
/// 将来 クリッピング / ブレンドモード / マスク / 調整レイヤー を足す場合も、
/// このクラスにプロパティを増やし、合成側（PaintDocument.Render）を拡張すれば足りる。
/// </summary>
public sealed class Layer : IDisposable, INotifyPropertyChanged
{
    private string _name;
    private bool _isVisible = true;
    private float _opacity = 1f;

    public Layer(int width, int height, string name)
    {
        _name = name;

        var info = new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
        Bitmap = new SKBitmap(info);

        using var canvas = new SKCanvas(Bitmap);
        canvas.Clear(SKColors.Transparent);
    }

    public SKBitmap Bitmap { get; }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    /// <summary>0.0〜1.0</summary>
    public float Opacity
    {
        get => _opacity;
        set => Set(ref _opacity, Math.Clamp(value, 0f, 1f));
    }

    public byte AlphaByte => (byte)Math.Clamp(_opacity * 255f, 0f, 255f);

    public long ApproximateBytes => (long)Bitmap.Width * Bitmap.Height * 4;

    /// <summary>同じ内容の新しいレイヤーを作る。</summary>
    public Layer Duplicate(string name)
    {
        var copy = new Layer(Bitmap.Width, Bitmap.Height, name)
        {
            IsVisible = IsVisible,
            Opacity = Opacity
        };

        using var canvas = new SKCanvas(copy.Bitmap);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src, FilterQuality = SKFilterQuality.None };
        canvas.DrawBitmap(Bitmap, 0, 0, paint);

        return copy;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose() => Bitmap.Dispose();
}
