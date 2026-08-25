using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AINEPaint.Drawing;

namespace AINEPaint.Views;

public partial class NewCanvasDialog : Window
{
    private bool _suppressPresetSync;

    public int CanvasWidth { get; private set; } = 1920;
    public int CanvasHeight { get; private set; } = 1080;
    public CanvasBackground Background { get; private set; } = CanvasBackground.White;

    public NewCanvasDialog()
    {
        InitializeComponent();
        PresetBox.SelectedIndex = 0;
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is not string tag || tag == "custom") return;

        var parts = tag.Split('x');
        _suppressPresetSync = true;
        WidthBox.Text = parts[0];
        HeightBox.Text = parts[1];
        _suppressPresetSync = false;
        Validate();
    }

    private void OnSizeTextChanged(object sender, TextChangedEventArgs e)
    {
        // 手入力されたらプリセットを「カスタム」に落とす
        if (!_suppressPresetSync && PresetBox is not null && IsLoaded)
        {
            var current = $"{WidthBox.Text}x{HeightBox.Text}";
            bool matchesPreset = false;
            foreach (ComboBoxItem candidate in PresetBox.Items)
                if (candidate.Tag as string == current) matchesPreset = true;

            if (!matchesPreset)
            {
                _suppressPresetSync = true;
                PresetBox.SelectedIndex = PresetBox.Items.Count - 1;
                _suppressPresetSync = false;
            }
        }
        Validate();
    }

    private bool Validate()
    {
        if (InfoText is null || OkButton is null) return false;

        bool okW = int.TryParse(WidthBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w);
        bool okH = int.TryParse(HeightBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h);

        if (!okW || !okH)
        {
            InfoText.Text = "幅と高さは半角数字で入力してください。";
            OkButton.IsEnabled = false;
            return false;
        }

        if (w < PaintDocument.MinSide || h < PaintDocument.MinSide ||
            w > PaintDocument.MaxSide || h > PaintDocument.MaxSide)
        {
            InfoText.Text = $"サイズは {PaintDocument.MinSide} 〜 {PaintDocument.MaxSide} px の範囲で指定してください。";
            OkButton.IsEnabled = false;
            return false;
        }

        double mb = (double)w * h * 4 / (1024 * 1024);
        InfoText.Text = $"推定メモリ: 約 {mb:0.#} MB（レイヤー1枚あたり）";
        OkButton.IsEnabled = true;
        return true;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (!Validate()) return;

        CanvasWidth = int.Parse(WidthBox.Text, CultureInfo.InvariantCulture);
        CanvasHeight = int.Parse(HeightBox.Text, CultureInfo.InvariantCulture);
        Background = TransparentBgRadio.IsChecked == true
            ? CanvasBackground.Transparent
            : CanvasBackground.White;

        DialogResult = true;
    }
}
