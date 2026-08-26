using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AINEPaint.Color;
using SkiaSharp;

namespace AINEPaint.Views;

public partial class ColorPickerDialog : Window
{
    /// <summary>各入力欄が互いを書き換え合って無限ループしないようにするための番人。</summary>
    private bool _syncing;

    public SKColor SelectedColor { get; private set; } = SKColors.Black;

    public ColorPickerDialog(SKColor initial)
    {
        InitializeComponent();

        SvBox.SelectionChanged += OnPickerChanged;
        HueBar.SelectionChanged += OnHueChanged;

        SetColor(initial, updateHexBox: true, updateRgbBoxes: true, updatePicker: true);
    }

    // ===== 各入力からの変更 =====

    private void OnHueChanged()
    {
        SvBox.Hue = HueBar.Hue;
        OnPickerChanged();
    }

    private void OnPickerChanged()
    {
        if (_syncing) return;

        var color = SKColor.FromHsv(HueBar.Hue, SvBox.Saturation * 100f, SvBox.Value * 100f);
        SetColor(color, updateHexBox: true, updateRgbBoxes: true, updatePicker: false);
    }

    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        if (!ColorUtil.TryParseHex(HexBox.Text, out var color)) return;

        SetColor(color, updateHexBox: false, updateRgbBoxes: true, updatePicker: true);
    }

    private void OnRgbChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        if (RBox is null || GBox is null || BBox is null) return;

        if (!TryParseByte(RBox.Text, out byte r) ||
            !TryParseByte(GBox.Text, out byte g) ||
            !TryParseByte(BBox.Text, out byte b))
            return;

        SetColor(new SKColor(r, g, b), updateHexBox: true, updateRgbBoxes: false, updatePicker: true);
    }

    private static bool TryParseByte(string? text, out byte value)
    {
        value = 0;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
               && parsed is >= 0 and <= 255
               && (value = (byte)parsed) == parsed;
    }

    // ===== 反映 =====

    private void SetColor(SKColor color, bool updateHexBox, bool updateRgbBoxes, bool updatePicker)
    {
        _syncing = true;
        try
        {
            SelectedColor = color;

            if (updatePicker)
            {
                color.ToHsv(out float h, out float s, out float v);
                HueBar.Hue = h;
                SvBox.Hue = h;
                SvBox.SetSaturationValue(s / 100f, v / 100f);
            }

            if (updateHexBox)
                HexBox.Text = ColorUtil.ToHex(color);

            if (updateRgbBoxes)
            {
                RBox.Text = color.Red.ToString(CultureInfo.InvariantCulture);
                GBox.Text = color.Green.ToString(CultureInfo.InvariantCulture);
                BBox.Text = color.Blue.ToString(CultureInfo.InvariantCulture);
            }

            PreviewSwatch.Background = new SolidColorBrush(ColorUtil.ToWpf(color));
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
