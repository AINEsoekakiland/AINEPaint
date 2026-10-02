using System.Windows;
using System.Windows.Controls;

namespace AINEPaint.Views;

public partial class RemoveBackgroundDialog : Window
{
    /// <summary>色の違いをどこまで背景とみなすか。</summary>
    public int Tolerance => (int)ToleranceSlider.Value;

    public RemoveBackgroundDialog(int initialTolerance)
    {
        InitializeComponent();

        ToleranceSlider.Value = Math.Clamp((double)initialTolerance,
            ToleranceSlider.Minimum, ToleranceSlider.Maximum);
    }

    private void OnToleranceChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ToleranceText is null) return;
        ToleranceText.Text = ((int)e.NewValue).ToString();
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
