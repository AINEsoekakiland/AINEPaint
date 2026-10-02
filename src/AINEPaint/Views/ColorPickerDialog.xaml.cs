using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AINEPaint.Color;
using SkiaSharp;

namespace AINEPaint.Views;

public partial class ColorPickerDialog : Window
{
    /// <summary>各入力欄が互いを書き換え合って無限ループしないようにするための番人。</summary>
    private bool _syncing;

    private readonly SKColor _original;

    /// <summary>
    /// 並べておく色。白黒と、絵で使うことの多い色をひととおり。
    /// 利用者が登録するものではないので、設定には保存しない。
    /// </summary>
    private static readonly string[] Swatches =
    {
        "#000000", "#4D4D4D", "#9A9A9A", "#FFFFFF",
        "#E03131", "#F08C00", "#F5D90A", "#2F9E44",
        "#1098AD", "#1C7ED6", "#6741D9", "#E64980",
        "#8B5A2B", "#C98A6B", "#F6C9A8", "#FFE8D6",
    };

    public SKColor SelectedColor { get; private set; } = SKColors.Black;

    public ColorPickerDialog(SKColor initial)
    {
        InitializeComponent();

        _original = initial;

        Wheel.SelectionChanged += OnWheelChanged;
        RedSlider.ValueChanged += OnSliderChanged;
        GreenSlider.ValueChanged += OnSliderChanged;
        BlueSlider.ValueChanged += OnSliderChanged;

        BuildSwatches();

        OriginalSwatch.Background = new SolidColorBrush(ColorUtil.ToWpf(initial));
        SetColor(initial, updateHex: true, updateSliders: true, updateWheel: true);
    }

    private void BuildSwatches()
    {
        foreach (var hex in Swatches)
        {
            if (!ColorUtil.TryParseHex(hex, out var color)) continue;

            var swatch = new Border
            {
                Width = 30,
                Height = 24,
                Margin = new Thickness(0, 0, 4, 4),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)FindResource("BorderBrush2"),
                Background = new SolidColorBrush(ColorUtil.ToWpf(color)),
                Cursor = Cursors.Hand,
                ToolTip = hex,
                Tag = color
            };

            swatch.MouseLeftButtonDown += OnSwatchClick;
            SwatchPanel.Children.Add(swatch);
        }
    }

    // ===== 各入力からの変更 =====

    private void OnWheelChanged()
    {
        if (_syncing) return;

        var color = SKColor.FromHsv(Wheel.Hue, Wheel.Saturation * 100f, Wheel.Value * 100f);
        SetColor(color, updateHex: true, updateSliders: true, updateWheel: false);
    }

    private void OnSliderChanged()
    {
        if (_syncing) return;

        var color = new SKColor(RedSlider.Value, GreenSlider.Value, BlueSlider.Value);
        SetColor(color, updateHex: true, updateSliders: false, updateWheel: true);
    }

    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        if (!ColorUtil.TryParseHex(HexBox.Text, out var color)) return;

        SetColor(color, updateHex: false, updateSliders: true, updateWheel: true);
    }

    private void OnSwatchClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: SKColor color }) return;

        SetColor(color, updateHex: true, updateSliders: true, updateWheel: true);
    }

    private void OnOriginalClick(object sender, MouseButtonEventArgs e)
        => SetColor(_original, updateHex: true, updateSliders: true, updateWheel: true);

    // ===== 反映 =====

    private void SetColor(SKColor color, bool updateHex, bool updateSliders, bool updateWheel)
    {
        _syncing = true;
        try
        {
            SelectedColor = color;

            if (updateWheel)
            {
                color.ToHsv(out float h, out float s, out float v);

                // 白や黒では色合いが決まらない。輪の位置が勝手に飛ばないよう、そのときは今の値を保つ
                if (s > 0.5f && v > 0.5f) Wheel.Hue = h;
                Wheel.SetSaturationValue(s / 100f, v / 100f);
            }

            if (updateSliders)
            {
                RedSlider.Value = color.Red;
                GreenSlider.Value = color.Green;
                BlueSlider.Value = color.Blue;
            }

            if (updateHex)
                HexBox.Text = ColorUtil.ToHex(color);

            UpdateSliderTracks(color);

            RedText.Text = color.Red.ToString();
            GreenText.Text = color.Green.ToString();
            BlueText.Text = color.Blue.ToString();

            PreviewSwatch.Background = new SolidColorBrush(ColorUtil.ToWpf(color));
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// 各スライダーの帯を「その成分だけを 0 から 255 まで動かした色」にする。
    /// 他の2つの成分は今の色のまま固定するので、動かした先の色がそのまま見える。
    /// </summary>
    private void UpdateSliderTracks(SKColor color)
    {
        RedSlider.SetTrack(new SKColor(0, color.Green, color.Blue),
                           new SKColor(255, color.Green, color.Blue));

        GreenSlider.SetTrack(new SKColor(color.Red, 0, color.Blue),
                             new SKColor(color.Red, 255, color.Blue));

        BlueSlider.SetTrack(new SKColor(color.Red, color.Green, 0),
                            new SKColor(color.Red, color.Green, 255));
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
