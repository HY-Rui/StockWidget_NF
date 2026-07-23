using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace StockWidget;

/// <summary>
/// 透明置顶浮窗（对应 Python FloatLabel）。
/// 动态构建表格：列开关 + 左对齐 + 颜色着色 + K线控件嵌入 + 拖拽 + 双击隐藏 + 右键菜单 + 列宽调整。
/// </summary>
public partial class FloatWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private readonly AppConfig _cfg;
    private readonly QuoteService _svc;
    private string[] _visibleHeaders = Array.Empty<string>();
    private int _klineCol = -1;
    private List<QuoteRow> _lastRows = new();

    // 颜色画刷
    private static readonly SolidColorBrush UpBrush = new((Color)ColorConverter.ConvertFromString(Constants.UpColor));
    private static readonly SolidColorBrush DownBrush = new((Color)ColorConverter.ConvertFromString(Constants.DownColor));
    private static readonly SolidColorBrush NeutralBrush = new((Color)ColorConverter.ConvertFromString(Constants.NeutralColor));

    // 回调
    public Action? RequestSettings { get; set; }
    public Action? RequestQuit { get; set; }
    public Action? Changed { get; set; }

    internal FloatWindow(AppConfig cfg)
    {
        _cfg = cfg;
        _svc = new QuoteService(cfg);
        _svc.Refreshed += OnRefreshed;

        // 计算可见列
        RebuildVisibleHeaders();

        InitializeComponent();
        SourceInitialized += (s, e) => ApplyMouseThrough();

        // 隐藏暂停刷新 / 显示恢复（对应 Python showEvent/hideEvent）
        IsVisibleChanged += (s, ev) =>
        {
            if (IsVisible) _svc.Start(); else _svc.Stop();
        };

        ApplyAppearance();

        // 刷新服务
        _svc.SetCodes(QuoteCodes());
        _svc.Start();
    }

    private List<string> QuoteCodes()
    {
        return _cfg.CheckedCodes
            .Concat(_cfg.Positions.Where(kv => kv.Value.IsValid).Select(kv => kv.Key))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<QuoteRow> DisplayRows(IEnumerable<QuoteRow> rows)
    {
        var checkedCodes = new HashSet<string>(_cfg.CheckedCodes, StringComparer.OrdinalIgnoreCase);
        return rows.Where(row => PositionKeys(row).Any(checkedCodes.Contains)).ToList();
    }

    private void RebuildVisibleHeaders()
    {
        _visibleHeaders = Constants.AllHeaders
            .Where(HeaderVisible)
            .Where(h => _cfg.Market != "futures" || !Constants.FuturesUnsupported.Contains(h))
            .ToArray();
        _klineCol = Array.IndexOf(_visibleHeaders, "K线");
    }

    // ===== 列可见性 =====

    private bool HeaderVisible(string h) => h switch
    {
        "代码" => _cfg.CodeVisible,
        "名称" => _cfg.NameVisible,
        "现价" => _cfg.PriceVisible,
        "涨跌值" => _cfg.ChangeVisible,
        "涨跌幅" => _cfg.ChangePctVisible,
        "买一" or "卖一" => _cfg.B1s1Visible,
        "委比" => _cfg.CommiVisible,
        "成交量" => _cfg.VolVisible,
        "成交额" => _cfg.AmountVisible,
        "均价" => _cfg.AvgVisible,
        "K线" => _cfg.KlineVisible,
        "持仓成本" => _cfg.PositionCostVisible,
        "持仓数量" => _cfg.PositionQuantityVisible,
        "持仓盈亏" => _cfg.PositionProfitVisible,
        "持仓盈亏率" => _cfg.PositionProfitPctVisible,
        _ => false,
    };

    // ===== 外观 =====

    private void ApplyAppearance()
    {
        // 背景
        var bg = _cfg.Bg;
        PanelBorder.Background = new SolidColorBrush(Color.FromArgb((byte)bg.A, (byte)bg.R, (byte)bg.G, (byte)bg.B));
        Opacity = _cfg.OpacityPct / 100.0;

        // 字体
        var ff = new FontFamily(_cfg.FontFamily);
        TextElement.SetFontFamily(RootPanel, ff);
        TextElement.SetFontSize(RootPanel, (double)_cfg.FontSize);
        ErrorLabel.FontFamily = ff;
        ErrorLabel.FontSize = _cfg.FontSize;
        PositionSummaryLabel.FontFamily = ff;
        PositionSummaryLabel.FontSize = _cfg.FontSize;

        // 网格边框
        TableGrid.ShowGridLines = false;
    }

    private void ApplyMouseThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        var nextStyle = _cfg.MouseThroughEnabled
            ? style | WsExTransparent
            : style & ~WsExTransparent;
        if (nextStyle != style)
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(nextStyle));
    }

    // ===== 刷新回调 =====

    private void OnRefreshed(QuoteResult result)
    {
        Dispatcher.Invoke(() =>
        {
            var allIsTheOnlyDisplayItem = _cfg.PositionSummaryVisible && _cfg.CheckedCodes.Count == 0;
            if (result.Status == QuoteStatus.NoCheckedCodes && allIsTheOnlyDisplayItem)
            {
                ErrorLabel.Visibility = Visibility.Collapsed;
                ErrorLabel.Text = "";
            }
            else if (result.Status != QuoteStatus.Ok)
            {
                ErrorLabel.Text = result.Message;
                ErrorLabel.Visibility = Visibility.Visible;
            }
            else if (_cfg.CheckedCodes.Count == 0 && !_cfg.PositionSummaryVisible)
            {
                ErrorLabel.Text = "未选择显示项";
                ErrorLabel.Visibility = Visibility.Visible;
            }
            else
            {
                ErrorLabel.Visibility = Visibility.Collapsed;
                ErrorLabel.Text = "";
            }

            if (result.Status == QuoteStatus.Ok)
            {
                _lastRows = result.Rows;
                RebuildTable(result.Rows);
            }
            else if (!result.KeepPreviousRows)
            {
                _lastRows.Clear();
                RebuildTable(_lastRows);
            }
        });
    }

    // ===== 表格构建 =====

    private void RebuildTable(List<QuoteRow> rows)
    {
        ClearTable();
        PositionSummaryLabel.Visibility = Visibility.Collapsed;
        PositionSummaryLabel.Text = "";
        var displayRows = DisplayRows(rows);

        var headers = _visibleHeaders;
        _klineCol = Array.IndexOf(headers, "K线");
        if (headers.Length == 0)
        {
            SizeToContent = SizeToContent.WidthAndHeight;
            return;
        }

        UpdatePositionCells(displayRows);
        var rowViews = displayRows.Select(r => new QuoteRowView(r, headers)).ToList();
        var summary = BuildPositionSummary(rows);
        var summaryLabelColumn = Array.FindIndex(headers, h => !IsPositionMetric(h));
        var canResizeColumns = _cfg.GridVisible && _cfg.ColumnResizeEnabled;

        // 列定义：内容列之间插入窄 GridSplitter，实现列宽记忆。
        for (int hIdx = 0; hIdx < headers.Length; hIdx++)
        {
            var h = headers[hIdx];
            var summaryText = SummaryCellText(h, hIdx, summaryLabelColumn, summary);
            var minWidth = MeasureColumnMinWidth(h, hIdx, rowViews, summaryText);
            var width = _cfg.ColWidths.TryGetValue(h, out var remembered) && remembered > 0
                ? Math.Max(remembered, minWidth)
                : minWidth;
            var cd = new ColumnDefinition { Width = new GridLength(width), MinWidth = minWidth };
            TableGrid.ColumnDefinitions.Add(cd);
            if (hIdx < headers.Length - 1)
            {
                TableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(canResizeColumns ? 5 : 0) });
            }
        }

        // 表头（可选）
        int rowIdx = 0;
        if (_cfg.HeaderVisible)
        {
            TableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < headers.Length; c++)
            {
                var tb = MakeHeaderCell(headers[c]);
                AddTableCell(tb, rowIdx, c * 2);
            }
            rowIdx++;
        }

        // 数据行
        var fontSize = TextElement.GetFontSize(RootPanel);
        double rowH = Math.Ceiling(Math.Max(fontSize * 1.35, fontSize * 1.95 + _cfg.LineExtraPx + 2));
        var fgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_cfg.Fg));

        foreach (var rowView in rowViews)
        {
            TableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rowH) });
            for (int c = 0; c < headers.Length; c++)
            {
                if (headers[c] == "K线")
                {
                    var k = new KLineControl
                    {
                        Width = 42, Height = rowH,
                        MinWidth = 38,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(3, 0, 3, 0),
                    };
                    k.UpdateScheme(_cfg.DefaultColor, fgBrush.Color);
                    k.SetPointSize(_cfg.FontSize);
                    k.SetData(rowView.KLine);
                    AddTableCell(k, rowIdx, c * 2);
                }
                else
                {
                    var text = rowView.CellText(c);
                    var tb = new TextBlock
                    {
                        Text = text,
                        Padding = new Thickness(4, 0, 4, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        FontFamily = TextElement.GetFontFamily(RootPanel),
                        FontSize = _cfg.FontSize,
                    };
                    tb.HorizontalAlignment = HorizontalAlignment.Left;

                    // 颜色着色
                    tb.Foreground = _cfg.DefaultColor
                        ? GetSignBrush(rowView.SignFor(c))
                        : fgBrush;
                    AddTableCell(tb, rowIdx, c * 2);
                }
            }
            rowIdx++;
        }

        if (_cfg.PositionSummaryVisible)
        {
            TableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rowH) });
            for (int c = 0; c < headers.Length; c++)
            {
                var header = headers[c];
                var tb = new TextBlock
                {
                    Text = SummaryCellText(header, c, summaryLabelColumn, summary),
                    Padding = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    FontFamily = TextElement.GetFontFamily(RootPanel),
                    FontSize = _cfg.FontSize,
                    Foreground = header is "持仓盈亏" or "持仓盈亏率"
                        ? SummaryBrush(summary.ProfitSign)
                        : fgBrush,
                };
                AddTableCell(tb, rowIdx, c * 2);
            }
            rowIdx++;
        }

        if (canResizeColumns)
            AddColumnSplitters(headers, rowIdx);
        SizeToContent = SizeToContent.Manual;
        SizeToContent = SizeToContent.WidthAndHeight;
    }

    private void ClearTable()
    {
        TableGrid.Children.Clear();
        TableGrid.RowDefinitions.Clear();
        TableGrid.ColumnDefinitions.Clear();
    }

    private PositionSummaryData BuildPositionSummary(IReadOnlyList<QuoteRow> rows)
    {
        var positions = _cfg.Positions.Where(kv => kv.Value.IsValid).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (positions.Count == 0)
        {
            return new PositionSummaryData(new Dictionary<string, string>
            {
                ["持仓成本"] = "---",
                ["持仓数量"] = "---",
                ["持仓盈亏"] = "---",
                ["持仓盈亏率"] = "---",
            }, 0);
        }

        var totalCost = positions.Values.Sum(pos => pos.CostPrice * pos.Quantity);
        var totalQuantity = positions.Values.Sum(pos => pos.Quantity);
        var parametersComplete = _cfg.Market != "futures" || positions.Keys.All(HasFuturesProductParameter);
        double pricedCost = 0;
        double totalProfit = 0;
        foreach (var row in rows)
        {
            var pos = FindPosition(row, positions);
            if (pos == null) continue;
            var price = CurrentPrice(row);
            if (price <= 0) continue;
            if (!TryPositionProfit(row, pos, price, out var profit)) continue;
            pricedCost += pos.CostPrice * pos.Quantity;
            totalProfit += profit;
        }

        var pct = pricedCost > 0 ? totalProfit / pricedCost * 100 : double.NaN;
        return new PositionSummaryData(new Dictionary<string, string>
        {
            ["持仓成本"] = FmtMoney(totalCost),
            ["持仓数量"] = totalQuantity.ToString("0.####", CultureInfo.InvariantCulture),
            ["持仓盈亏"] = parametersComplete && pricedCost > 0 ? FmtMoney(totalProfit) : "---",
            ["持仓盈亏率"] = parametersComplete && double.IsFinite(pct) ? $"{pct:+0.00;-0.00;0.00}%" : "---",
        }, parametersComplete && pricedCost > 0 ? Math.Sign(totalProfit) : 0);
    }

    private static bool IsPositionMetric(string header) =>
        header is "持仓成本" or "持仓数量" or "持仓盈亏" or "持仓盈亏率";

    private static string SummaryCellText(
        string header,
        int column,
        int labelColumn,
        PositionSummaryData summary)
    {
        if (column == labelColumn) return "All";
        return summary.Values.TryGetValue(header, out var value) ? value : "";
    }

    private sealed record PositionSummaryData(Dictionary<string, string> Values, int ProfitSign);

    private void UpdatePositionCells(IReadOnlyList<QuoteRow> rows)
    {
        var positions = _cfg.Positions.Where(kv => kv.Value.IsValid).ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var row in rows)
        {
            SetCell(row, "持仓成本", "");
            SetCell(row, "持仓数量", "");
            SetCell(row, "持仓盈亏", "");
            SetCell(row, "持仓盈亏率", "");
            row.PositionProfit = 0;

            var pos = FindPosition(row, positions);
            if (pos == null) continue;

            SetCell(row, "持仓成本", FmtMoney(pos.CostPrice));
            SetCell(row, "持仓数量", pos.Quantity.ToString("0.####", CultureInfo.InvariantCulture));

            var price = CurrentPrice(row);
            if (price <= 0)
            {
                SetCell(row, "持仓盈亏", "---");
                SetCell(row, "持仓盈亏率", "---");
                continue;
            }

            var cost = pos.CostPrice * pos.Quantity;
            if (!TryPositionProfit(row, pos, price, out var profit))
            {
                SetCell(row, "持仓盈亏", "---");
                SetCell(row, "持仓盈亏率", "---");
                continue;
            }
            var pct = cost > 0 ? profit / cost * 100 : 0;
            row.PositionProfit = Math.Sign(profit);
            SetCell(row, "持仓盈亏", FmtMoney(profit));
            SetCell(row, "持仓盈亏率", $"{pct:+0.00;-0.00;0.00}%");
        }
    }

    private static void SetCell(QuoteRow row, string header, string value)
    {
        var index = Array.IndexOf(Constants.AllHeaders, header);
        if (index >= 0 && index < row.Cells.Length) row.Cells[index] = value;
    }

    private PositionInfo? FindPosition(QuoteRow row, Dictionary<string, PositionInfo> positions)
    {
        foreach (var key in PositionKeys(row))
            if (positions.TryGetValue(key, out var pos)) return pos;
        return null;
    }

    private bool HasFuturesProductParameter(string contract)
    {
        var product = FuturesProductCode.FromContract(contract);
        return product != null &&
               _cfg.TryGetFuturesProductParameter(product, out var parameter) &&
               parameter.IsValid;
    }

    private bool TryPositionProfit(QuoteRow row, PositionInfo position, double price, out double profit)
    {
        if (_cfg.Market != "futures")
        {
            profit = position.ProfitAt(price);
            return true;
        }

        var product = FuturesProductCode.FromContract(row.Code) ??
                      FuturesProductCode.FromContract(row.Cells[0]);
        if (product == null ||
            !_cfg.TryGetFuturesProductParameter(product, out var parameter) ||
            !parameter.IsValid)
        {
            profit = 0;
            return false;
        }

        var priceChange = position.IsShort ? position.CostPrice - price : price - position.CostPrice;
        var ticks = priceChange / parameter.TickSize;
        profit = ticks * parameter.TickProfit * position.Quantity;
        return double.IsFinite(profit);
    }

    private IEnumerable<string> PositionKeys(QuoteRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.Code)) yield return row.Code;
        var displayCode = row.Cells[0].Trim();
        if (!string.IsNullOrWhiteSpace(displayCode))
        {
            yield return displayCode;
            if (_cfg.Market == "futures" && !displayCode.StartsWith("nf_", StringComparison.OrdinalIgnoreCase))
                yield return "nf_" + displayCode.ToUpperInvariant();
        }
    }

    private static double CurrentPrice(QuoteRow row)
    {
        if (row.KLine is { } k && k.C > 0) return k.C;
        var text = RegexPrice().Match(row.Cells[2]).Value;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static string FmtMoney(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private Brush SummaryBrush(int sign)
    {
        if (!_cfg.DefaultColor)
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(_cfg.Fg));
        return sign > 0 ? UpBrush : sign < 0 ? DownBrush : NeutralBrush;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"-?\d+(\.\d+)?")]
    private static partial System.Text.RegularExpressions.Regex RegexPrice();

    private void AddTableCell(UIElement child, int row, int column)
    {
        UIElement element = child;
        if (_cfg.GridVisible)
        {
            element = new Border
            {
                BorderBrush = GridLineBrush(),
                BorderThickness = new Thickness(0.5),
                Child = child,
                SnapsToDevicePixels = true
            };
        }

        TableGrid.Children.Add(element);
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
    }

    private Brush GridLineBrush()
    {
        try
        {
            var fg = (Color)ColorConverter.ConvertFromString(_cfg.Fg);
            return new SolidColorBrush(Color.FromArgb(90, fg.R, fg.G, fg.B));
        }
        catch
        {
            return new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
        }
    }

    private double MeasureColumnMinWidth(
        string header,
        int visibleIndex,
        IReadOnlyList<QuoteRowView> rows,
        string summaryText)
    {
        if (header == "K线") return Math.Max(48, _cfg.FontSize * 3.2);

        var fontFamily = TextElement.GetFontFamily(RootPanel);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double maxWidth = MeasureTextWidth(header, fontFamily, FontWeights.SemiBold, pixelsPerDip);
        foreach (var row in rows)
        {
            maxWidth = Math.Max(maxWidth, MeasureTextWidth(row.CellText(visibleIndex), fontFamily, FontWeights.Normal, pixelsPerDip));
        }
        maxWidth = Math.Max(maxWidth, MeasureTextWidth(summaryText, fontFamily, FontWeights.Normal, pixelsPerDip));
        return Math.Ceiling(maxWidth + 12);
    }

    private double MeasureTextWidth(string text, FontFamily fontFamily, FontWeight weight, double pixelsPerDip)
    {
        var formatted = new FormattedText(
            text ?? "",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(fontFamily, FontStyles.Normal, weight, FontStretches.Normal),
            _cfg.FontSize,
            Brushes.White,
            pixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private void AddColumnSplitters(string[] headers, int rowSpan)
    {
        for (int c = 0; c < headers.Length - 1; c++)
        {
            var columnIndex = c;
            var leftHeader = headers[c];
            var rightHeader = headers[c + 1];
            var splitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ResizeDirection = GridResizeDirection.Columns,
            };
            splitter.DragCompleted += (s, e) =>
            {
                var leftCol = TableGrid.ColumnDefinitions[columnIndex * 2];
                var rightCol = TableGrid.ColumnDefinitions[(columnIndex + 1) * 2];
                if (leftCol.ActualWidth > 0)
                {
                    var width = (int)Math.Round(Math.Max(leftCol.MinWidth, leftCol.ActualWidth));
                    _cfg.ColWidths[leftHeader] = width;
                    leftCol.Width = new GridLength(width);
                }
                if (rightCol.ActualWidth > 0)
                {
                    var width = (int)Math.Round(Math.Max(rightCol.MinWidth, rightCol.ActualWidth));
                    _cfg.ColWidths[rightHeader] = width;
                    rightCol.Width = new GridLength(width);
                }
                Changed?.Invoke();
            };
            TableGrid.Children.Add(splitter);
            Grid.SetRow(splitter, 0);
            Grid.SetRowSpan(splitter, Math.Max(1, rowSpan));
            Grid.SetColumn(splitter, c * 2 + 1);
        }
    }

    private TextBlock MakeHeaderCell(string header)
    {
        var fgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_cfg.Fg));
        var tb = new TextBlock
        {
            Text = header,
            Padding = new Thickness(4, 2, 4, 2),
            FontWeight = FontWeights.SemiBold,
            FontFamily = TextElement.GetFontFamily(RootPanel),
            FontSize = _cfg.FontSize,
            Foreground = _cfg.DefaultColor ? NeutralBrush : fgBrush,
        };
        tb.HorizontalAlignment = HorizontalAlignment.Left;
        return tb;
    }

    private SolidColorBrush GetSignBrush(int sign)
    {
        return sign > 0 ? UpBrush : (sign < 0 ? DownBrush : NeutralBrush);
    }

    // ===== 拖拽 / 双击 =====

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount > 1) return;
            try
            {
                DragMove();
                KeepInsideWorkArea();
                Changed?.Invoke();
            }
            catch (InvalidOperationException)
            {
                // DragMove can fail if the mouse is released before WPF starts the move loop.
            }
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            ShowContextMenu();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e) { }
    private void OnMouseUp(object sender, MouseButtonEventArgs e) { }

    private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) RequestSettings?.Invoke();
    }

    private void KeepInsideWorkArea()
    {
        var bounds = CurrentWorkArea();
        var w = EffectiveSize(ActualWidth, Width);
        var h = EffectiveSize(ActualHeight, Height);
        var maxLeft = bounds.Right - w;
        var maxTop = bounds.Bottom - h;

        var left = Math.Max(bounds.Left, Math.Min(Left, maxLeft));
        var top = Math.Max(bounds.Top, Math.Min(Top, maxTop));

        if (_cfg.SnapEnabled && _cfg.SnapDistance > 0)
        {
            var snapDistance = _cfg.SnapDistance;
            if (Math.Abs(left - bounds.Left) <= snapDistance)
                left = bounds.Left;
            else if (Math.Abs(maxLeft - left) <= snapDistance)
                left = maxLeft;

            if (Math.Abs(top - bounds.Top) <= snapDistance)
                top = bounds.Top;
            else if (Math.Abs(maxTop - top) <= snapDistance)
                top = maxTop;
        }

        Left = left;
        Top = top;
    }

    private Rect CurrentWorkArea()
    {
        var centerX = Left + EffectiveSize(ActualWidth, Width) / 2;
        var centerY = Top + EffectiveSize(ActualHeight, Height) / 2;
        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var centerDevice = toDevice.Transform(new Point(centerX, centerY));

        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var b = screen.WorkingArea;
            if (centerDevice.X >= b.Left && centerDevice.X <= b.Right && centerDevice.Y >= b.Top && centerDevice.Y <= b.Bottom)
            {
                var topLeft = fromDevice.Transform(new Point(b.Left, b.Top));
                var bottomRight = fromDevice.Transform(new Point(b.Right, b.Bottom));
                return new Rect(topLeft, bottomRight);
            }
        }
        return SystemParameters.WorkArea;
    }

    private static double EffectiveSize(double actual, double requested)
    {
        if (double.IsFinite(actual) && actual > 0) return actual;
        if (double.IsFinite(requested) && requested > 0) return requested;
        return 1;
    }

    public void EnsureVisible()
    {
        Dispatcher.BeginInvoke(
            new Action(KeepInsideWorkArea),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ===== 右键菜单 =====

    private void ShowContextMenu()
    {
        var menu = new ContextMenu();

        // 显示指标子菜单
        var subCols = new MenuItem { Header = "显示指标" };
        var indicatorGroups = new[]
        {
            (Title: "盘口指标", Headers: new[] { "现价", "涨跌值", "涨跌幅", "买一", "委比", "成交量", "成交额", "均价" }),
            (Title: "持仓指标", Headers: new[] { "持仓成本", "持仓数量", "持仓盈亏", "持仓盈亏率" }),
            (Title: "其他指标", Headers: new[] { "代码", "名称", "K线" }),
        };
        foreach (var group in indicatorGroups)
        {
            var category = new MenuItem { Header = group.Title };
            foreach (var name in group.Headers)
            {
                var label = name == "买一" ? "买一/卖一" : name;
                var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = HeaderVisible(name) };
                item.Click += (s, e) => ToggleColumn(name);
                if (_cfg.Market == "futures" && Constants.FuturesUnsupported.Contains(name))
                    item.IsEnabled = false;
                category.Items.Add(item);
            }
            subCols.Items.Add(category);
        }
        menu.Items.Add(subCols);

        var miRefresh = new MenuItem { Header = "刷新" };
        miRefresh.Click += (s, e) => RefreshNow();
        menu.Items.Add(miRefresh);

        var miMouseThrough = new MenuItem { Header = "鼠标穿透", IsCheckable = true, IsChecked = _cfg.MouseThroughEnabled };
        miMouseThrough.Click += (s, e) =>
        {
            _cfg.MouseThroughEnabled = !_cfg.MouseThroughEnabled;
            ApplyMouseThrough();
            Changed?.Invoke();
        };
        menu.Items.Add(miMouseThrough);

        menu.Items.Add(new Separator());

        // 设置
        var miSet = new MenuItem { Header = "设置…" };
        miSet.Click += (s, e) => RequestSettings?.Invoke();
        menu.Items.Add(miSet);

        menu.Items.Add(new Separator());

        // 隐藏浮窗
        var miHide = new MenuItem { Header = "隐藏浮窗" };
        miHide.Click += (s, e) => Hide();
        menu.Items.Add(miHide);

        // 退出
        if (RequestQuit != null)
        {
            var miQuit = new MenuItem { Header = "退出" };
            miQuit.Click += (s, e) => RequestQuit?.Invoke();
            menu.Items.Add(miQuit);
        }

        menu.IsOpen = true;
    }

    private void ToggleColumn(string name)
    {
        var attr = Constants.HeaderAttrs[name];
        switch (attr)
        {
            case "code_visible": _cfg.CodeVisible = !_cfg.CodeVisible; break;
            case "name_visible": _cfg.NameVisible = !_cfg.NameVisible; break;
            case "price_visible": _cfg.PriceVisible = !_cfg.PriceVisible; break;
            case "change_visible": _cfg.ChangeVisible = !_cfg.ChangeVisible; break;
            case "change_pct_visible": _cfg.ChangePctVisible = !_cfg.ChangePctVisible; break;
            case "b1s1_visible": _cfg.B1s1Visible = !_cfg.B1s1Visible; break;
            case "commi_visible": _cfg.CommiVisible = !_cfg.CommiVisible; break;
            case "vol_visible": _cfg.VolVisible = !_cfg.VolVisible; break;
            case "amount_visible": _cfg.AmountVisible = !_cfg.AmountVisible; break;
            case "avg_visible": _cfg.AvgVisible = !_cfg.AvgVisible; break;
            case "kline_visible": _cfg.KlineVisible = !_cfg.KlineVisible; break;
            case "position_cost_visible": _cfg.PositionCostVisible = !_cfg.PositionCostVisible; break;
            case "position_quantity_visible": _cfg.PositionQuantityVisible = !_cfg.PositionQuantityVisible; break;
            case "position_profit_visible": _cfg.PositionProfitVisible = !_cfg.PositionProfitVisible; break;
            case "position_profit_pct_visible": _cfg.PositionProfitPctVisible = !_cfg.PositionProfitPctVisible; break;
        }
        Changed?.Invoke();
        // 重建可见列并刷新表格
        RebuildVisibleHeaders();
        RebuildLast();
    }

    private void RebuildLast()
    {
        if (_lastRows.Count > 0)
            RebuildTable(_lastRows);
        else
            _svc.RefreshNow();
    }

    private void RefreshNow()
    {
        _svc.SetCodes(QuoteCodes());
        _svc.RefreshNow();
    }

    // ===== 配置更新入口（设置面板调用） =====

    public void ApplyConfigChanges()
    {
        ApplyAppearance();
        ApplyMouseThrough();
        RebuildVisibleHeaders();
        _svc.SetInterval(_cfg.RefreshSeconds);
        _svc.SetCodes(QuoteCodes());
        RebuildLast();
        Dispatcher.BeginInvoke(new Action(KeepInsideWorkArea), System.Windows.Threading.DispatcherPriority.Background);
    }

    // 切换显示/隐藏（热键和托盘调用）
    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _svc.Dispose();
        base.OnClosed(e);
    }

    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, index)
            : new IntPtr(GetWindowLong32(hwnd, index));
    }

    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, index, value)
            : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
}
