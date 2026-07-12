using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace StockWidget;

internal sealed class PositionEditDialog : Window
{
    private readonly TextBox _code = new();
    private readonly TextBox _cost = new();
    private readonly TextBox _quantity = new();
    private readonly ComboBox _direction = new();
    private readonly bool _allowShort;

    public string CodeText => _code.Text.Trim();
    public double? CostPrice { get; private set; }
    public double? Quantity { get; private set; }
    public string Direction { get; private set; } = "long";

    public PositionEditDialog(string code, PositionInfo? position, bool allowShort)
    {
        _allowShort = allowShort;
        Title = "编辑自选";
        Width = 300;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _code.Text = code;
        _cost.Text = position?.CostPrice.ToString("0.####", CultureInfo.InvariantCulture) ?? "";
        _quantity.Text = position?.Quantity.ToString("0.####", CultureInfo.InvariantCulture) ?? "";
        _direction.Items.Add(new ComboBoxItem { Content = "做多", Tag = "long" });
        _direction.Items.Add(new ComboBoxItem { Content = "做空", Tag = "short" });
        _direction.SelectedIndex = allowShort && position?.IsShort == true ? 1 : 0;

        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var buttonRow = allowShort ? 4 : 3;
        for (int i = 0; i <= buttonRow; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(root, 0, "代码：", _code);
        AddRow(root, 1, "成本价：", _cost);
        AddRow(root, 2, "数量：", _quantity);
        if (allowShort) AddRow(root, 3, "方向：", _direction);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "确认", Width = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 72, IsCancel = true };
        ok.Click += OnOk;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, buttonRow);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        _code.SelectAll();
        _code.Focus();
    }

    private static void AddRow(Grid root, int row, string label, Control input)
    {
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 8) };
        input.Margin = new Thickness(0, 0, 0, 8);
        input.MinWidth = 170;
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        root.Children.Add(lbl);
        root.Children.Add(input);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CodeText))
        {
            MessageBox.Show(this, "代码不能为空。", "编辑自选", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParseOptional(_cost.Text, out var cost) || !TryParseOptional(_quantity.Text, out var qty))
        {
            MessageBox.Show(this, "成本价和数量请输入数字，或留空表示不持仓。", "编辑自选", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CostPrice = cost;
        Quantity = qty;
        Direction = _allowShort && _direction.SelectedItem is ComboBoxItem item && item.Tag?.ToString() == "short"
            ? "short"
            : "long";
        DialogResult = true;
    }

    private static bool TryParseOptional(string text, out double? value)
    {
        value = null;
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return true;
        text = text.Replace(',', '.');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            value = v;
            return true;
        }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
        {
            value = v;
            return true;
        }
        return false;
    }
}
