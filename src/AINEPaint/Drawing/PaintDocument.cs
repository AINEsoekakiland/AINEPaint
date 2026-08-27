using System.ComponentModel;
using AINEPaint.Layers;
using SkiaSharp;

namespace AINEPaint.Drawing;

/// <summary>キャンバスの背景種別。</summary>
public enum CanvasBackground
{
    White,
    Transparent
}

/// <summary>
/// 1枚の作品を表すモデル。レイヤーの集まりとして持つ。
///
/// レイヤーの画素は常に透明で始める。「白背景」は表示・書き出し時に敷くもので、
/// 画素には焼き込まない。焼き込むと消しゴムとPNG透明書き出しが両立しない。
///
/// 配列の 0 番が一番下のレイヤー。UI 側では上下を反転して見せる。
/// </summary>
public sealed class PaintDocument : IDisposable
{
    /// <summary>安全側に倒した1辺の上限。メモリ量は 幅×高さ×4バイト×レイヤー枚数。</summary>
    public const int MaxSide = 6000;
    public const int MinSide = 16;

    private readonly List<Layer> _layers = new();
    private int _activeLayerIndex;

    public PaintDocument(int width, int height, CanvasBackground background)
        : this(width, height, background, createInitialLayer: true)
    {
    }

    private PaintDocument(int width, int height, CanvasBackground background, bool createInitialLayer)
    {
        if (width < MinSide || width > MaxSide)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height < MinSide || height > MaxSide)
            throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        Background = background;

        if (createInitialLayer)
            AddLayerInternal(new Layer(width, height, "レイヤー 1"), 0);

        _activeLayerIndex = 0;
    }

    /// <summary>
    /// 読み込んだレイヤーからドキュメントを作る。ファイル読み込み専用。
    /// レイヤーは下から順に渡すこと。
    /// </summary>
    public static PaintDocument FromLayers(int width, int height, CanvasBackground background,
                                           IEnumerable<Layer> layers, int activeIndex)
    {
        var document = new PaintDocument(width, height, background, createInitialLayer: false);

        foreach (var layer in layers)
            document.AddLayerInternal(layer, document._layers.Count);

        if (document._layers.Count == 0)
            document.AddLayerInternal(new Layer(width, height, "レイヤー 1"), 0);

        document._activeLayerIndex = Math.Clamp(activeIndex, 0, document._layers.Count - 1);
        return document;
    }

    public int Width { get; }
    public int Height { get; }
    public CanvasBackground Background { get; }

    public IReadOnlyList<Layer> Layers => _layers;

    /// <summary>レイヤー構成・並び・選択が変わったときに発火。</summary>
    public event Action? StructureChanged;

    /// <summary>表示状態や不透明度など、再描画が要る変更で発火。</summary>
    public event Action? ContentChanged;

    public int ActiveLayerIndex
    {
        get => _activeLayerIndex;
        set
        {
            int clamped = Math.Clamp(value, 0, Math.Max(0, _layers.Count - 1));
            if (clamped == _activeLayerIndex) return;
            _activeLayerIndex = clamped;
            StructureChanged?.Invoke();
        }
    }

    public Layer? ActiveLayer =>
        _activeLayerIndex >= 0 && _activeLayerIndex < _layers.Count ? _layers[_activeLayerIndex] : null;

    public long ApproximateMemoryBytes => (long)Width * Height * 4 * Math.Max(1, _layers.Count);

    // ===== レイヤー操作 =====

    public Layer AddLayer()
    {
        var layer = new Layer(Width, Height, NextLayerName());
        AddLayerInternal(layer, _activeLayerIndex + 1);
        _activeLayerIndex = _layers.IndexOf(layer);
        StructureChanged?.Invoke();
        return layer;
    }

    public Layer? DuplicateActiveLayer()
    {
        if (ActiveLayer is not { } source) return null;

        var copy = source.Duplicate($"{source.Name} のコピー");
        AddLayerInternal(copy, _activeLayerIndex + 1);
        _activeLayerIndex = _layers.IndexOf(copy);
        StructureChanged?.Invoke();
        return copy;
    }

    /// <summary>最後の1枚は削除できない。削除できたら true。</summary>
    public bool RemoveActiveLayer()
    {
        if (_layers.Count <= 1) return false;

        var layer = _layers[_activeLayerIndex];
        layer.PropertyChanged -= OnLayerPropertyChanged;
        _layers.RemoveAt(_activeLayerIndex);

        // 削除したレイヤーは履歴が参照している可能性があるので Dispose しない
        _activeLayerIndex = Math.Clamp(_activeLayerIndex, 0, _layers.Count - 1);
        StructureChanged?.Invoke();
        return true;
    }

    /// <summary>選択中のレイヤーを 1 つ上（または下）へ動かす。</summary>
    public bool MoveActiveLayer(int offset)
    {
        int target = _activeLayerIndex + offset;
        if (target < 0 || target >= _layers.Count) return false;

        (_layers[_activeLayerIndex], _layers[target]) = (_layers[target], _layers[_activeLayerIndex]);
        _activeLayerIndex = target;
        StructureChanged?.Invoke();
        return true;
    }

    private void AddLayerInternal(Layer layer, int index)
    {
        index = Math.Clamp(index, 0, _layers.Count);
        _layers.Insert(index, layer);
        layer.PropertyChanged += OnLayerPropertyChanged;
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Layer.IsVisible) or nameof(Layer.Opacity))
            ContentChanged?.Invoke();
        else
            StructureChanged?.Invoke();
    }

    private string NextLayerName()
    {
        int n = _layers.Count + 1;
        while (_layers.Any(l => l.Name == $"レイヤー {n}")) n++;
        return $"レイヤー {n}";
    }

    // ===== 履歴用のレイヤー構成の出し入れ =====

    public (List<Layer> Layers, int ActiveIndex) SnapshotStructure()
        => (new List<Layer>(_layers), _activeLayerIndex);

    public void RestoreStructure(List<Layer> layers, int activeIndex)
    {
        foreach (var layer in _layers)
            layer.PropertyChanged -= OnLayerPropertyChanged;

        _layers.Clear();
        _layers.AddRange(layers);

        foreach (var layer in _layers)
            layer.PropertyChanged += OnLayerPropertyChanged;

        _activeLayerIndex = Math.Clamp(activeIndex, 0, Math.Max(0, _layers.Count - 1));
        StructureChanged?.Invoke();
    }

    // ===== 合成 =====

    /// <summary>
    /// 全レイヤーを下から順に合成する。背景は含めない（表示側・書き出し側の責務）。
    /// activeOverlay を渡すと、選択中レイヤーの上に重ねて合成する（描画中のプレビュー用）。
    /// </summary>
    public void Render(SKCanvas canvas, SKBitmap? activeOverlay = null, SKPaint? overlayPaint = null,
                       SKFilterQuality quality = SKFilterQuality.None, SKPath? overlayClip = null)
    {
        foreach (var layer in _layers)
        {
            if (!layer.IsVisible || layer.AlphaByte == 0) continue;

            bool withOverlay = activeOverlay is not null && ReferenceEquals(layer, ActiveLayer);

            using var layerPaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(layer.AlphaByte),
                FilterQuality = quality
            };

            if (!withOverlay)
            {
                canvas.DrawBitmap(layer.Bitmap, 0, 0, layerPaint);
                continue;
            }

            // 描画中のストロークは、そのレイヤーの中で合成してから
            // レイヤー不透明度を掛ける必要がある。消しゴムが下のレイヤーを
            // 削ってしまわないのもこのグループ化のおかげ。
            canvas.SaveLayer(layerPaint);
            using (var full = new SKPaint { FilterQuality = quality })
                canvas.DrawBitmap(layer.Bitmap, 0, 0, full);
            if (overlayClip is not null)
            {
                canvas.Save();
                canvas.ClipPath(overlayClip, SKClipOperation.Intersect, antialias: true);
                canvas.DrawBitmap(activeOverlay, 0, 0, overlayPaint);
                canvas.Restore();
            }
            else
            {
                canvas.DrawBitmap(activeOverlay, 0, 0, overlayPaint);
            }

            canvas.Restore();
        }
    }

    /// <summary>指定座標の合成後の色を求める。スポイト用。</summary>
    public SKColor SamplePixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return SKColors.Transparent;

        float r = 0f, g = 0f, b = 0f, a = 0f;

        if (Background == CanvasBackground.White)
        {
            r = g = b = 255f;
            a = 1f;
        }

        foreach (var layer in _layers)
        {
            if (!layer.IsVisible || layer.AlphaByte == 0) continue;

            var src = layer.Bitmap.GetPixel(x, y);
            float sa = src.Alpha / 255f * layer.Opacity;
            if (sa <= 0f) continue;

            r = src.Red * sa + r * (1f - sa);
            g = src.Green * sa + g * (1f - sa);
            b = src.Blue * sa + b * (1f - sa);
            a = sa + a * (1f - sa);
        }

        if (a <= 0f) return SKColors.Transparent;

        return new SKColor(
            (byte)Math.Clamp(r, 0f, 255f),
            (byte)Math.Clamp(g, 0f, 255f),
            (byte)Math.Clamp(b, 0f, 255f),
            (byte)Math.Clamp(a * 255f, 0f, 255f));
    }

    public void Dispose()
    {
        foreach (var layer in _layers)
        {
            layer.PropertyChanged -= OnLayerPropertyChanged;
            layer.Dispose();
        }
        _layers.Clear();
    }
}
