using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AINEPaint.Brushes;
using AINEPaint.Color;
using AINEPaint.Drawing;
using AINEPaint.Views;
using SkiaSharp;

namespace AINEPaint;

public partial class MainWindow : Window
{
    private PaintDocument? _document;

    public MainWindow()
    {
        InitializeComponent();
        Canvas.ViewStateChanged += UpdateStatus;
        Canvas.ColorPicked += ApplyBrushColor;
        ApplyBrushColor(SKColors.Black);
        UpdateStatus();
    }

    // ===== ファイル =====

    private void OnNewCanvasClick(object sender, RoutedEventArgs e) => CreateNewCanvas();

    private void CreateNewCanvas()
    {
        var dialog = new NewCanvasDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var created = new PaintDocument(dialog.CanvasWidth, dialog.CanvasHeight, dialog.Background);

        _document?.Dispose();
        _document = created;

        Canvas.Document = created;
        EmptyHint.Visibility = Visibility.Collapsed;
        UpdateStatus();
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    // ===== 表示 =====

    private void OnZoomInClick(object sender, RoutedEventArgs e) => Canvas.ZoomByStep(1.25f);
    private void OnZoomOutClick(object sender, RoutedEventArgs e) => Canvas.ZoomByStep(1f / 1.25f);
    private void OnFitClick(object sender, RoutedEventArgs e) => Canvas.FitToWindow();

    // ===== ツール =====

    private void OnToolChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        ApplyTool(tag);
    }

    private void ApplyTool(string tag)
    {
        // Canvas は InitializeComponent 中の IsChecked 設定でも呼ばれ得るので防御する
        if (Canvas is null) return;

        Canvas.PanToolActive = tag == "Pan";
        Canvas.EyedropperActive = tag == "Picker";

        switch (tag)
        {
            case "Pen":
                Canvas.Brush.Kind = BrushKind.Pen;
                break;
            case "Pencil":
                Canvas.Brush.Kind = BrushKind.Pencil;
                break;
            case "Eraser":
                Canvas.Brush.Kind = BrushKind.Eraser;
                break;
        }
    }

    /// <summary>キーボードからツールを切り替える。ボタンの選択状態も合わせる。</summary>
    private void SelectTool(string tag)
    {
        if (ToolPanel is null) return;

        foreach (var child in ToolPanel.Children)
        {
            if (child is RadioButton { Tag: string t } button && t == tag && button.IsEnabled)
            {
                button.IsChecked = true;   // Checked イベント経由で ApplyTool が走る
                return;
            }
        }
    }

    // ===== 色 =====

    private void OnColorButtonClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ColorPickerDialog(Canvas.Brush.Color) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        ApplyBrushColor(dialog.SelectedColor);
    }

    private void ApplyBrushColor(SKColor color)
    {
        if (Canvas is null || ColorButton is null) return;

        Canvas.Brush.Color = color;
        ColorButton.Background = new SolidColorBrush(ColorUtil.ToWpf(color));
    }

    // ===== ブラシ設定 =====

    private void OnSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Canvas is null || SizeValueText is null) return;

        Canvas.Brush.Size = (float)e.NewValue;
        SizeValueText.Text = ((int)e.NewValue).ToString();
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Canvas is null || OpacityValueText is null) return;

        Canvas.Brush.Opacity = (float)(e.NewValue / 100.0);
        OpacityValueText.Text = $"{(int)e.NewValue}%";
    }

    private void NudgeBrushSize(double delta)
    {
        SizeSlider.Value = Math.Clamp(SizeSlider.Value + delta, SizeSlider.Minimum, SizeSlider.Maximum);
    }

    // ===== 入力 =====

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.N:
                    CreateNewCanvas();
                    e.Handled = true;
                    return;
                case Key.OemPlus:
                case Key.Add:
                    Canvas.ZoomByStep(1.25f);
                    e.Handled = true;
                    return;
                case Key.OemMinus:
                case Key.Subtract:
                    Canvas.ZoomByStep(1f / 1.25f);
                    e.Handled = true;
                    return;
                case Key.D0:
                case Key.NumPad0:
                    Canvas.FitToWindow();
                    e.Handled = true;
                    return;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.P: SelectTool("Pen"); e.Handled = true; return;
            case Key.N: SelectTool("Pencil"); e.Handled = true; return;
            case Key.E: SelectTool("Eraser"); e.Handled = true; return;
            case Key.H: SelectTool("Pan"); e.Handled = true; return;
            case Key.I: SelectTool("Picker"); e.Handled = true; return;

            case Key.OemOpenBrackets:
                NudgeBrushSize(-Math.Max(1, SizeSlider.Value * 0.1));
                e.Handled = true;
                return;
            case Key.OemCloseBrackets:
                NudgeBrushSize(Math.Max(1, SizeSlider.Value * 0.1));
                e.Handled = true;
                return;

            case Key.Space:
                Canvas.IsPanModifierDown = true;
                e.Handled = true;   // ボタンにフォーカスがある場合の誤爆を防ぐ
                return;
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Key == Key.Space)
            Canvas.IsPanModifierDown = false;
    }

    // ===== ステータスバー =====

    private void UpdateStatus()
    {
        if (StatusText is null) return;

        if (_document is null)
        {
            StatusText.Text = "キャンバスなし";
            return;
        }

        string background = _document.Background == CanvasBackground.Transparent ? "透明" : "白";
        StatusText.Text = $"{_document.Width} × {_document.Height} px　背景: {background}　" +
                          $"ズーム: {Canvas.Viewport.Scale * 100:0}%";
    }
}
