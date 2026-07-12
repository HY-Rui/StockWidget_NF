using System.Windows;

namespace StockWidget;

/// <summary>简单文本输入对话框（用于代码添加/编辑）。</summary>
public partial class InputDialog : Window
{
    public string InputText => Input.Text;

    public InputDialog(string title, string prompt, string defaultText = "")
    {
        InitializeComponent();
        Title = title;
        Prompt.Text = prompt;
        Input.Text = defaultText;
        Input.SelectAll();
        Input.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Input.Text))
            DialogResult = true;
    }
}
