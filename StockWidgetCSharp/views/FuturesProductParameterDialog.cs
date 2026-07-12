using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace StockWidget;

internal sealed class FuturesProductParameterDialog : Window
{
    private readonly TextBox _tickSize = new();
    private readonly TextBox _tickProfit = new();

    public double TickSize { get; private set; }
    public double TickProfit { get; private set; }

    public FuturesProductParameterDialog(string product, FuturesProductParameter? parameter)
    {
        Title = $"品种参数 - {product}";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _tickSize.Text = parameter?.TickSize > 0
            ? parameter.TickSize.ToString("0.####", CultureInfo.InvariantCulture)
            : "1";
        _tickProfit.Text = parameter?.TickProfit > 0
            ? parameter.TickProfit.ToString("0.####", CultureInfo.InvariantCulture)
            : "";

        var root = new Grid { Margin = new Thickness(12) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(root, 0, "每跳价差：", _tickSize);
        AddRow(root, 1, "每跳毛利：", _tickProfit);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button { Content = "确认", Width = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 72, IsCancel = true };
        ok.Click += OnOk;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        _tickProfit.Focus();
        _tickProfit.SelectAll();
    }

    private static void AddRow(Grid root, int row, string label, TextBox input)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        };
        input.MinWidth = 170;
        input.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        root.Children.Add(text);
        root.Children.Add(input);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!TryPositive(_tickSize.Text, out var tickSize) ||
            !TryPositive(_tickProfit.Text, out var tickProfit))
        {
            MessageBox.Show(this, "每跳价差和每跳毛利都必须是正数。", "期货品种参数", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TickSize = tickSize;
        TickProfit = tickProfit;
        DialogResult = true;
    }

    private static bool TryPositive(string text, out double value)
    {
        text = text.Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0;
    }
}
