using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StockWidget;

internal sealed class FuturesProductParametersDialog : Window
{
    private readonly AppConfig _config;
    private readonly Action _changed;
    private readonly ListBox _list = new();

    public FuturesProductParametersDialog(AppConfig config, Action changed)
    {
        _config = config;
        _changed = changed;
        Title = "品种参数";
        Width = 620;
        Height = 520;
        MinHeight = 300;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(12) };
        var close = new Button
        {
            Content = "关闭",
            Width = 76,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            IsCancel = true
        };
        close.Click += (s, e) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var header = new Grid { Margin = new Thickness(7, 0, 7, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(new TextBlock { Text = "合约品种", Foreground = Brushes.DimGray });
        var codeHeader = new TextBlock { Text = "代码缩写", Foreground = Brushes.DimGray };
        Grid.SetColumn(codeHeader, 1);
        header.Children.Add(codeHeader);
        var sizeHeader = new TextBlock { Text = "每跳价差(点)", Foreground = Brushes.DimGray };
        Grid.SetColumn(sizeHeader, 2);
        header.Children.Add(sizeHeader);
        var profitHeader = new TextBlock { Text = "每跳毛利(元)", Foreground = Brushes.DimGray };
        Grid.SetColumn(profitHeader, 3);
        header.Children.Add(profitHeader);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _list.BorderBrush = Brushes.LightGray;
        _list.BorderThickness = new Thickness(1);
        _list.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        root.Children.Add(_list);

        Content = root;
        RebuildList();
    }

    private void RebuildList()
    {
        _list.Items.Clear();
        var products = FuturesProductCatalog.All.Select(spec => spec.Code)
            .Concat(_config.FutCodes.Select(FuturesProductCode.FromContract))
            .Concat(_config.FutProductParameters.Keys.Select(FuturesProductCode.FromContract))
            .Where(product => product != null)
            .Select(product => product!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var product in products)
        {
            _config.TryGetFuturesProductParameter(product, out var parameter);
            FuturesProductCatalog.TryGet(product, out var spec);
            var row = new Grid { Margin = new Thickness(4, 2, 4, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = spec?.Name ?? product, VerticalAlignment = VerticalAlignment.Center });
            var code = new TextBlock { Text = product, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(code, 1);
            row.Children.Add(code);
            var tickSize = new TextBlock
            {
                Text = parameter?.IsValid == true ? parameter.TickSize.ToString("0.####") : "---",
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(tickSize, 2);
            row.Children.Add(tickSize);
            var profit = new TextBlock
            {
                Text = parameter?.IsValid == true ? parameter.TickProfit.ToString("0.####") : "---",
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(profit, 3);
            row.Children.Add(profit);

            var item = new ListBoxItem { Content = row, Tag = product, Padding = new Thickness(2) };
            item.MouseDoubleClick += (s, e) => EditParameter(product);
            item.ContextMenu = MakeContextMenu(product, _config.FutProductParameters.ContainsKey(product));
            _list.Items.Add(item);
        }
    }

    private ContextMenu MakeContextMenu(string product, bool hasCustomParameter)
    {
        var menu = new ContextMenu();
        var edit = new MenuItem { Header = "编辑参数" };
        edit.Click += (s, e) => EditParameter(product);
        menu.Items.Add(edit);

        var clear = new MenuItem { Header = "恢复默认", IsEnabled = hasCustomParameter };
        clear.Click += (s, e) =>
        {
            _config.FutProductParameters.Remove(product);
            RebuildList();
            _changed();
        };
        menu.Items.Add(clear);
        return menu;
    }

    private void EditParameter(string product)
    {
        _config.TryGetFuturesProductParameter(product, out var parameter);
        var dialog = new FuturesProductParameterDialog(product, parameter) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _config.FutProductParameters[product] = new FuturesProductParameter
        {
            TickSize = dialog.TickSize,
            TickProfit = dialog.TickProfit
        };
        RebuildList();
        _changed();
    }
}
