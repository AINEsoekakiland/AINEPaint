using System.Windows;

namespace AINEPaint.Views;

/// <summary>使い方の説明。内容は実際に実装されている機能だけを書くこと。</summary>
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
