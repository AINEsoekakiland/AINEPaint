using System.Windows;

namespace AINEPaint.Views;

/// <summary>1行のテキストを入力させる汎用ダイアログ。</summary>
public partial class TextInputDialog : Window
{
    public TextInputDialog(string prompt, string initialValue)
    {
        InitializeComponent();

        PromptText.Text = prompt;
        InputBox.Text = initialValue;

        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public string Value => InputBox.Text.Trim();

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InputBox.Text)) return;
        DialogResult = true;
    }
}
