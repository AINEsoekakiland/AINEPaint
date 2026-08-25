using System.Windows;
using System.Windows.Input;
using AINEPaint.Drawing;
using AINEPaint.Views;

namespace AINEPaint;

public partial class MainWindow : Window
{
    private PaintDocument? _document;

    public MainWindow()
    {
        InitializeComponent();
        Canvas.ViewStateChanged += UpdateStatus;
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
        }

        if (e.Key == Key.Space)
        {
            Canvas.IsPanModifierDown = true;
            e.Handled = true; // ボタンにフォーカスがある場合の誤爆を防ぐ
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
