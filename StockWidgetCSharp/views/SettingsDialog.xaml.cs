using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace StockWidget;

/// <summary>
/// 设置对话框（对应 Python SettingPanel.SettingsDialog）。
/// 四页签：自选列表 / 显示数据 / 外观 / 常规。
/// 控件在 code-behind 动态构建，逻辑对齐 Python 版。
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly AppConfig _cfg;
    private readonly AppConfig _targetConfig;
    private readonly AppConfig _original;
    private readonly FloatWindow _win;
    private readonly Action? _saveCallback;
    private readonly Action<string?>? _setIconCallback;
    private readonly Func<string, bool>? _setHotkeyCallback;

    // Tab0 控件
    private ComboBox _cmbMarket = new();
    private TextBlock _lblMarketHint = new();
    private ListBox _listCodes = new();
    // Tab1 控件
    private ComboBox _cmbDataMarket = new();
    private ComboBox _cmbInterval = new();
    private readonly Dictionary<string, CheckBox> _colCheckboxes = new();
    private CheckBox _cbShortCode = new();
    private ComboBox _cmbNameLength = new();
    private CheckBox _cbFutAbbrev = new();
    private ComboBox _cmbB1s1Display = new();
    // Tab2 控件
    private CheckBox _chkTableHeader = new();
    private CheckBox _chkTableGrid = new();
    private CheckBox _chkColumnResize = new();
    private CheckBox _chkDefaultColor = new();
    private Slider _sliderBgAlpha = new();
    private TextBlock _lblBgAlpha = new();
    private Slider _sliderWinOpacity = new();
    private TextBlock _lblWinOpacity = new();
    private ComboBox _cmbFamily = new();
    private Slider _sliderFont = new();
    private TextBlock _lblFont = new();
    private Slider _sliderLine = new();
    private TextBlock _lblLine = new();
    // Tab3 控件
    private CheckBox _chkStartOnBoot = new();
    private CheckBox _chkSnapEnabled = new();
    private CheckBox _chkMouseThrough = new();
    private Slider _sliderSnapDistance = new();
    private TextBlock _lblSnapDistance = new();
    private TextBox _txtHotkey = new();
    private ComboBox _cmbIcon = new();

    private bool _suppressChange;
    private bool _accepted;
    private bool _hotkeyChanged;

    // 代码归一化正则（对齐 Python SettingPanel）
    private static readonly Regex ReFull = new(@"^(sh|sz|bj)\d+$");
    private static readonly Regex Re6 = new(@"^\d{6}$");
    private static readonly Regex ReFutures = new(@"^nf_[a-zA-Z]{1,3}\d{3,4}$", RegexOptions.IgnoreCase);

    // 刷新间隔档位
    private static readonly double[] Intervals = { 0.1, 0.2, 0.3, 0.5, 1, 2, 3, 5, 10, 15, 30, 60 };

    internal SettingsDialog(AppConfig cfg, FloatWindow win, Action? saveCallback, Action<string?>? setIconCallback, Func<string, bool>? setHotkeyCallback)
    {
        _targetConfig = cfg;
        _cfg = cfg.Clone();
        _original = cfg.Clone();
        _win = win;
        _saveCallback = saveCallback;
        _setIconCallback = setIconCallback;
        _setHotkeyCallback = setHotkeyCallback;
        InitializeComponent();
        RestoreWindowPosition();
        BuildAllTabs();
    }

    private void BuildAllTabs()
    {
        BuildTabCodes();
        BuildTabData();
        BuildTabAppearance();
        BuildTabGeneral();
    }

    // ====================== Tab0：自选列表 ======================

    private void BuildTabCodes()
    {
        var tab = new TabItem { Header = "自选列表" };
        var panel = new Grid { Margin = new Thickness(10) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 市场
        var gMarket = new GroupBox { Header = "市场", Margin = new Thickness(0, 0, 0, 8) };
        var marketPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
        _cmbMarket.Width = 136;
        _cmbMarket.Items.Add(new ComboBoxItem { Content = "股票", Tag = "stock" });
        _cmbMarket.Items.Add(new ComboBoxItem { Content = "国内期货", Tag = "futures" });
        _cmbMarket.SelectedIndex = _cfg.Market == "futures" ? 1 : 0;
        _cmbMarket.SelectionChanged += OnMarketChanged;
        marketPanel.Children.Add(new TextBlock { Text = "品种：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        marketPanel.Children.Add(_cmbMarket);
        _lblMarketHint.Foreground = Brushes.Gray;
        _lblMarketHint.VerticalAlignment = VerticalAlignment.Center;
        _lblMarketHint.Margin = new Thickness(12, 0, 0, 0);
        marketPanel.Children.Add(_lblMarketHint);
        gMarket.Content = marketPanel;
        Grid.SetRow(gMarket, 0);
        panel.Children.Add(gMarket);
        UpdateMarketHint();

        // 自选列表
        var gCodes = new GroupBox { Header = "自选列表", Margin = new Thickness(0) };
        var codesPanel = new Grid { Margin = new Thickness(2) };
        codesPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        codesPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _listCodes.Width = double.NaN;
        _listCodes.Height = double.NaN;
        _listCodes.MinHeight = 260;
        _listCodes.VerticalAlignment = VerticalAlignment.Stretch;
        _listCodes.BorderThickness = new Thickness(0);
        _listCodes.Background = Brushes.White;
        _listCodes.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Visible);
        _listCodes.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        _listCodes.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _listCodes.Items.SortDescriptions.Clear();
        RebuildCodesList();
        // ListBoxItem 双击编辑
        _listCodes.MouseDoubleClick += (s, e) =>
        {
            var code = GetSelectedCode();
            if (code != null) EditCodeAndPosition(code);
        };
        var listFrame = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = _listCodes,
            SnapsToDevicePixels = true
        };
        Grid.SetColumn(listFrame, 0);
        codesPanel.Children.Add(listFrame);

        var btnCol = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        string[] btnTexts = { "添加", "删除", "上移", "下移", "品种参数" };
        var btns = new Button[5];
        for (int i = 0; i < btnTexts.Length; i++)
        {
            var b = new Button
            {
                Content = btnTexts[i],
                Width = 76,
                Margin = new Thickness(0, 0, 0, i == btnTexts.Length - 1 ? 0 : 8)
            };
            btns[i] = b;
            btnCol.Children.Add(b);
        }
        btns[0].Click += (s, e) => AddCode();
        btns[1].Click += (s, e) => DelCode();
        btns[2].Click += (s, e) => MoveCode(-1);
        btns[3].Click += (s, e) => MoveCode(1);
        btns[4].Click += (s, e) => OpenFuturesProductParameters();
        Grid.SetColumn(btnCol, 1);
        codesPanel.Children.Add(btnCol);

        gCodes.Content = codesPanel;
        Grid.SetRow(gCodes, 1);
        panel.Children.Add(gCodes);

        tab.Content = panel;
        Tabs.Items.Add(tab);
    }

    private void UpdateMarketHint()
    {
        if (_cfg.Market == "futures")
        {
            _lblMarketHint.Text = "如 nf_RB2610";
            _lblMarketHint.ToolTip = "国内期货统一用 nf_ 前缀\n示例：nf_RB2610(螺纹) nf_M2609(豆粕)\nnf_IF2606(股指) nf_TA2609(PTA) nf_SI2612(工业硅)";
        }
        else
        {
            _lblMarketHint.Text = "如 600000";
            _lblMarketHint.ToolTip = "股票代码：sh/sz/bj 前缀或6位数字\n示例：600000(浦发), 000001(平安), sh000001(上证)";
        }
    }

    private void RebuildCodesList()
    {
        _suppressChange = true;
        _listCodes.Items.Clear();

        var allCheckBox = new CheckBox
        {
            Content = "All",
            IsChecked = _cfg.PositionSummaryVisible,
            FontWeight = FontWeights.SemiBold,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 1, 2, 1)
        };
        allCheckBox.Checked += (s, e) =>
        {
            if (_suppressChange) return;
            _cfg.PositionSummaryVisible = true;
            NotifySave();
        };
        allCheckBox.Unchecked += (s, e) =>
        {
            if (_suppressChange) return;
            _cfg.PositionSummaryVisible = false;
            NotifySave();
        };
        var allItem = new ListBoxItem { Content = allCheckBox, Tag = null };
        allCheckBox.PreviewMouseDown += (s, e) => allItem.IsSelected = true;
        _listCodes.Items.Add(allItem);

        foreach (var c in _cfg.Codes)
        {
            var cb = new CheckBox
            {
                Content = c,
                IsChecked = _cfg.CheckedCodes.Contains(c),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 1, 2, 1),
                Foreground = HasPosition(c) ? Brushes.Red : Brushes.Black,
            };
            if (HasPosition(c) && _cfg.Positions.TryGetValue(c, out var pos))
                cb.ToolTip = _cfg.Market == "futures"
                    ? $"{pos.DirectionText}，持仓成本 {pos.CostPrice:0.####}，数量 {pos.Quantity:0.####}"
                    : $"持仓成本 {pos.CostPrice:0.####}，数量 {pos.Quantity:0.####}";
            cb.Checked += OnCodeCheckChanged;
            cb.Unchecked += OnCodeCheckChanged;
            var item = new ListBoxItem { Content = cb, Tag = c };
            item.ContextMenu = MakeCodeContextMenu(c);
            cb.PreviewMouseDown += (s, e) => item.IsSelected = true;
            cb.GotFocus += (s, e) => item.IsSelected = true;
            _listCodes.Items.Add(item);
        }
        _suppressChange = false;
    }

    private void OpenFuturesProductParameters()
    {
        var dialog = new FuturesProductParametersDialog(_cfg, NotifySave) { Owner = this };
        dialog.ShowDialog();
    }

    private bool HasPosition(string code) => _cfg.Positions.TryGetValue(code, out var pos) && pos.IsValid;

    private ContextMenu MakeCodeContextMenu(string code)
    {
        var menu = new ContextMenu();
        var edit = new MenuItem { Header = "编辑自选" };
        edit.Click += (s, e) => EditCodeAndPosition(code);
        menu.Items.Add(edit);

        var clear = new MenuItem { Header = "清除持仓", IsEnabled = HasPosition(code) };
        clear.Click += (s, e) =>
        {
            _cfg.Positions.Remove(code);
            RebuildCodesList();
            NotifySave();
        };
        menu.Items.Add(clear);
        return menu;
    }

    private string? GetSelectedCode()
    {
        return _listCodes.SelectedItem is ListBoxItem item ? item.Tag?.ToString() : null;
    }

    private void OnCodeCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressChange || sender is not CheckBox cb) return;
        var code = cb.Content?.ToString();
        if (string.IsNullOrWhiteSpace(code)) return;
        if (cb.IsChecked == true)
        {
            if (!_cfg.CheckedCodes.Contains(code)) _cfg.CheckedCodes.Add(code);
        }
        else
        {
            _cfg.CheckedCodes.Remove(code);
        }
        SyncCheckedOrder();
        NotifySave();
    }

    private void SyncCheckedOrder()
    {
        var checkedSet = _cfg.CheckedCodes.ToHashSet();
        _cfg.CheckedCodes.Clear();
        foreach (var c in _cfg.Codes)
            if (checkedSet.Contains(c)) _cfg.CheckedCodes.Add(c);
    }

    private void OnMarketChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressChange) return;
        if (_cmbMarket.SelectedItem is not ComboBoxItem item) return;
        var mode = item.Tag?.ToString() == "futures" ? "futures" : "stock";
        _cfg.Market = mode;
        UpdateMarketHint();
        // 切换市场后重建列表
        RebuildCodesList();
        ReloadDataControlsFromConfig();
        UpdateMarketSensitiveControls();
        NotifySave();
    }

    private void AddCode()
    {
        var code = _cfg.Market == "futures" ? "nf_RB2610" : "sh000001";
        var dlg = new InputDialog("添加代码", "请输入代码，可批量粘贴：", code)
        {
            Owner = this
        };
        if (dlg.ShowDialog() == true)
        {
            var added = 0;
            var invalid = 0;
            var normalized = NormalizeMany(dlg.InputText, out var failed);
            foreach (var norm in normalized)
            {
                if (!_cfg.Codes.Contains(norm))
                {
                    _cfg.Codes.Add(norm);
                    _cfg.CheckedCodes.Add(norm);
                    added++;
                }
            }
            invalid += failed;
            if (invalid > 0)
                MessageBox.Show(this, $"已跳过 {invalid} 个无效代码。", "添加代码", MessageBoxButton.OK, MessageBoxImage.Information);
            if (added > 0)
            {
                SyncCheckedOrder();
                RebuildCodesList();
                NotifySave();
            }
        }
    }

    private void DelCode()
    {
        var code = GetSelectedCode();
        if (code != null)
        {
            _cfg.Codes.Remove(code);
            _cfg.CheckedCodes.Remove(code);
            _cfg.Positions.Remove(code);
            SyncCheckedOrder();
            RebuildCodesList();
            NotifySave();
        }
    }

    private void MoveCode(int delta)
    {
        var code = GetSelectedCode();
        if (code == null) return;
        int idx = _cfg.Codes.IndexOf(code);
        int newIdx = idx + delta;
        if (idx < 0 || newIdx < 0 || newIdx >= _cfg.Codes.Count) return;
        (_cfg.Codes[newIdx], _cfg.Codes[idx]) = (_cfg.Codes[idx], _cfg.Codes[newIdx]);
        SyncCheckedOrder();
        RebuildCodesList();
        _listCodes.SelectedIndex = newIdx + 1;
        NotifySave();
    }

    private void EditCodeAndPosition(string oldCode)
    {
        _cfg.Positions.TryGetValue(oldCode, out var oldPosition);
        var dlg = new PositionEditDialog(oldCode, oldPosition, _cfg.Market == "futures") { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var norm = NormalizeCode(dlg.CodeText);
            if (norm != null && (norm == oldCode || !_cfg.Codes.Contains(norm)))
            {
                int idx = _cfg.Codes.IndexOf(oldCode);
                if (idx >= 0)
                {
                    bool wasChecked = _cfg.CheckedCodes.Contains(oldCode);
                    _cfg.Codes[idx] = norm;
                    if (wasChecked) { _cfg.CheckedCodes.Remove(oldCode); if (!_cfg.CheckedCodes.Contains(norm)) _cfg.CheckedCodes.Add(norm); }
                    _cfg.Positions.Remove(oldCode);
                    if (dlg.CostPrice is > 0 && dlg.Quantity is > 0)
                        _cfg.Positions[norm] = new PositionInfo
                        {
                            CostPrice = dlg.CostPrice.Value,
                            Quantity = dlg.Quantity.Value,
                            Direction = _cfg.Market == "futures" ? dlg.Direction : "long"
                        };
                    SyncCheckedOrder();
                    RebuildCodesList();
                    NotifySave();
                }
            }
            else if (norm == null)
            {
                MessageBox.Show(this, "代码格式无效。", "编辑自选", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private List<string> NormalizeMany(string input, out int invalidCount)
    {
        invalidCount = 0;
        var result = new List<string>();
        var seen = new HashSet<string>();
        foreach (var raw in Regex.Split(input, @"[\s,;，；]+"))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var norm = NormalizeCode(raw);
            if (norm == null)
            {
                invalidCount++;
                continue;
            }
            if (seen.Add(norm)) result.Add(norm);
        }
        return result;
    }

    /// <summary>代码归一化（对齐 Python _normalize_code_or_none）。</summary>
    private string? NormalizeCode(string s)
    {
        s = s.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (_cfg.Market == "futures")
        {
            if (ReFutures.IsMatch(s)) return "nf_" + s[3..].ToUpperInvariant();
            return null;
        }
        // 股票
        s = Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");
        if (string.IsNullOrEmpty(s)) return null;
        if (ReFull.IsMatch(s)) return s;
        if (Re6.IsMatch(s))
        {
            if (s[0] == '6' || s.StartsWith("90") || s[0] == '5') return "sh" + s;
            if (s[0] == '0' || s[0] == '3' || s[0] == '2' || s[0] == '1') return "sz" + s;
            if (s[0] == '8' || s[0] == '4' || s.StartsWith("92")) return "bj" + s;
        }
        return null;
    }

    // ====================== Tab1：显示数据 ======================

    private void BuildTabData()
    {
        var tab = new TabItem { Header = "显示数据" };
        var panel = new StackPanel { Margin = new Thickness(10) };

        // 刷新间隔
        var gInterval = new GroupBox { Header = "刷新间隔", Margin = new Thickness(0, 0, 0, 8) };
        _cmbDataMarket.Width = 104;
        _cmbDataMarket.Items.Add(new ComboBoxItem { Content = "股票", Tag = "stock" });
        _cmbDataMarket.Items.Add(new ComboBoxItem { Content = "国内期货", Tag = "futures" });
        _cmbDataMarket.SelectedIndex = _cfg.Market == "futures" ? 1 : 0;
        _cmbDataMarket.SelectionChanged += (s, e) =>
        {
            if (_suppressChange || _cmbDataMarket.SelectedIndex < 0) return;
            _cmbMarket.SelectedIndex = _cmbDataMarket.SelectedIndex;
        };
        _cmbInterval.Width = 136;
        foreach (var s in Intervals)
        {
            var label = s != (int)s ? $"{s} 秒" : $"{(int)s} 秒";
            _cmbInterval.Items.Add(new ComboBoxItem { Content = label, Tag = s });
        }
        // 匹配当前刷新间隔（宽容精度，避免 0.2 浮点误差导致不匹配）
        int sel = 1;  // 默认 0.2 秒
        for (int i = 0; i < Intervals.Length; i++)
            if (Math.Abs(Intervals[i] - _cfg.RefreshSeconds) < 0.01) { sel = i; break; }
        _cmbInterval.SelectedIndex = sel;
        _cmbInterval.SelectionChanged += (s, e) =>
        {
            if (_suppressChange) return;
            if (_cmbInterval.SelectedItem is ComboBoxItem item && item.Tag is double v)
            {
                _cfg.RefreshSeconds = v;
                NotifySave();
            }
        };
        var intervalPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
        intervalPanel.Children.Add(new TextBlock { Text = "市场：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _cmbDataMarket.Margin = new Thickness(0, 0, 24, 0);
        intervalPanel.Children.Add(_cmbDataMarket);
        intervalPanel.Children.Add(new TextBlock { Text = "刷新间隔：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        intervalPanel.Children.Add(_cmbInterval);
        gInterval.Content = intervalPanel;
        panel.Children.Add(gInterval);

        // 显示指标：三类横向排列，分类名在左、指标开关在右。
        var gFlags = new GroupBox { Header = "显示指标", Margin = new Thickness(0, 0, 0, 8) };
        var flagsGrid = new Grid { Margin = new Thickness(8, 6, 8, 8) };
        for (int i = 0; i < 3; i++)
            flagsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gFlags.Content = flagsGrid;

        Grid MakeFlagSection(string title, IEnumerable<string> headers)
        {
            var section = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            section.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            section.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var titleBlock = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DimGray,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 10, 0),
            };
            section.Children.Add(titleBlock);

            var indicators = new WrapPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(indicators, 1);
            section.Children.Add(indicators);
            foreach (var h in headers)
            {
                if (h == "卖一") continue;
                var label = h == "买一" ? "买一/卖一" : h;
                var cb = new CheckBox
                {
                    Content = label,
                    Margin = new Thickness(0, 2, 16, 2),
                    IsChecked = HeaderVisible(h),
                    IsEnabled = _cfg.Market != "futures" || !Constants.FuturesUnsupported.Contains(h),
                };
                var hh = h;  // 闭包捕获
                cb.Checked += (s, e) => OnColToggle(hh, true);
                cb.Unchecked += (s, e) => OnColToggle(hh, false);
                _colCheckboxes[h] = cb;
                indicators.Children.Add(cb);
            }
            return section;
        }

        var sections = new[]
        {
            MakeFlagSection("盘口指标", new[] { "现价", "涨跌值", "涨跌幅", "买一", "委比", "成交量", "成交额", "均价" }),
            MakeFlagSection("持仓指标", new[] { "持仓成本", "持仓数量", "持仓盈亏", "持仓盈亏率" }),
            MakeFlagSection("其他指标", new[] { "代码", "名称", "K线" }),
        };
        for (int i = 0; i < sections.Length; i++)
        {
            flagsGrid.Children.Add(sections[i]);
            Grid.SetRow(sections[i], i);
        }

        // 附加选项区（根据市场和列开关动态启用）
        var gExtra = new GroupBox { Header = "显示选项", Margin = new Thickness(0, 0, 0, 0) };
        var extraWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
        gExtra.Content = extraWrap;

        // 仅显示数字
        _cbShortCode.Content = "仅显示数字";
        _cbShortCode.IsChecked = _cfg.ShortCode;
        _cbShortCode.IsEnabled = _cfg.CodeVisible;
        _cbShortCode.Margin = new Thickness(0, 4, 24, 4);
        _cbShortCode.Checked += (s, e) => { if (!_suppressChange) { _cfg.ShortCode = true; NotifySave(); } };
        _cbShortCode.Unchecked += (s, e) => { if (!_suppressChange) { _cfg.ShortCode = false; NotifySave(); } };
        extraWrap.Children.Add(_cbShortCode);

        // 名称长度
        _cmbNameLength.Width = 80;
        foreach (var l in new[] { 0, 1, 2, 3, 4 })
            _cmbNameLength.Items.Add(new ComboBoxItem { Content = l > 0 ? $"{l}个字" : "完整", Tag = l });
        int nli = -1;
        for (int i = 0; i < 5; i++) if ((int)(_cmbNameLength.Items[i] as ComboBoxItem)!.Tag! == _cfg.NameLength) { nli = i; break; }
        _cmbNameLength.SelectedIndex = nli >= 0 ? nli : 0;
        _cmbNameLength.SelectionChanged += (s, e) =>
        {
            if (_suppressChange) return;
            if (_cmbNameLength.SelectedItem is ComboBoxItem item && item.Tag is int v)
            { _cfg.NameLength = v; NotifySave(); }
        };
        var nameLenPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 24, 4) };
        nameLenPanel.Name = "NameLengthPanel";
        nameLenPanel.Children.Add(new TextBlock { Text = "名称长度：", VerticalAlignment = VerticalAlignment.Center });
        nameLenPanel.Children.Add(_cmbNameLength);
        extraWrap.Children.Add(nameLenPanel);

        // 缩写(期货)
        _cbFutAbbrev.Content = "缩写(期货)";
        _cbFutAbbrev.IsChecked = _cfg.FutAbbrev;
        UpdateAbbrevEnabled();
        _cbFutAbbrev.Margin = new Thickness(0, 4, 24, 4);
        _cbFutAbbrev.Checked += (s, e) => { if (!_suppressChange) { _cfg.FutAbbrev = true; NotifySave(); } };
        _cbFutAbbrev.Unchecked += (s, e) => { if (!_suppressChange) { _cfg.FutAbbrev = false; NotifySave(); } };
        extraWrap.Children.Add(_cbFutAbbrev);

        // 买一/卖一显示模式
        _cmbB1s1Display.Width = 100;
        _cmbB1s1Display.Items.Add(new ComboBoxItem { Content = "数量", Tag = "qty" });
        _cmbB1s1Display.Items.Add(new ComboBoxItem { Content = "价格", Tag = "price" });
        _cmbB1s1Display.Items.Add(new ComboBoxItem { Content = "数量和价格", Tag = "both" });
        for (int i = 0; i < 3; i++)
            if ((_cmbB1s1Display.Items[i] as ComboBoxItem)!.Tag!.ToString() == _cfg.B1s1Display) { _cmbB1s1Display.SelectedIndex = i; break; }
        _cmbB1s1Display.SelectionChanged += (s, e) =>
        {
            if (_suppressChange) return;
            if (_cmbB1s1Display.SelectedItem is ComboBoxItem item && item.Tag is string v)
            { _cfg.B1s1Display = v; NotifySave(); }
        };
        var b1s1Panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        b1s1Panel.Children.Add(new TextBlock { Text = "买卖一：", VerticalAlignment = VerticalAlignment.Center });
        b1s1Panel.Children.Add(_cmbB1s1Display);
        extraWrap.Children.Add(b1s1Panel);

        panel.Children.Add(gFlags);
        panel.Children.Add(gExtra);
        UpdateMarketSensitiveControls();
        tab.Content = panel;
        Tabs.Items.Add(tab);
    }

    private bool HeaderVisible(string h) => h switch
    {
        "代码" => _cfg.CodeVisible, "名称" => _cfg.NameVisible,
        "现价" => _cfg.PriceVisible, "涨跌值" => _cfg.ChangeVisible, "涨跌幅" => _cfg.ChangePctVisible,
        "买一" or "卖一" => _cfg.B1s1Visible, "委比" => _cfg.CommiVisible,
        "成交量" => _cfg.VolVisible, "成交额" => _cfg.AmountVisible, "均价" => _cfg.AvgVisible,
        "K线" => _cfg.KlineVisible,
        "持仓成本" => _cfg.PositionCostVisible,
        "持仓数量" => _cfg.PositionQuantityVisible,
        "持仓盈亏" => _cfg.PositionProfitVisible,
        "持仓盈亏率" => _cfg.PositionProfitPctVisible,
        _ => false,
    };

    private void OnColToggle(string header, bool val)
    {
        if (_suppressChange) return;
        switch (header)
        {
            case "代码": _cfg.CodeVisible = val; break;
            case "名称": _cfg.NameVisible = val; break;
            case "现价": _cfg.PriceVisible = val; break;
            case "涨跌值": _cfg.ChangeVisible = val; break;
            case "涨跌幅": _cfg.ChangePctVisible = val; break;
            case "买一": _cfg.B1s1Visible = val; break;
            case "委比": _cfg.CommiVisible = val; break;
            case "成交量": _cfg.VolVisible = val; break;
            case "成交额": _cfg.AmountVisible = val; break;
            case "均价": _cfg.AvgVisible = val; break;
            case "K线": _cfg.KlineVisible = val; break;
            case "持仓成本": _cfg.PositionCostVisible = val; break;
            case "持仓数量": _cfg.PositionQuantityVisible = val; break;
            case "持仓盈亏": _cfg.PositionProfitVisible = val; break;
            case "持仓盈亏率": _cfg.PositionProfitPctVisible = val; break;
        }
        UpdateMarketSensitiveControls();
        NotifySave();
    }

    private void ReloadDataControlsFromConfig()
    {
        if (_colCheckboxes.Count == 0) return;

        _suppressChange = true;
        _cmbDataMarket.SelectedIndex = _cfg.Market == "futures" ? 1 : 0;
        foreach (var (header, checkBox) in _colCheckboxes)
            checkBox.IsChecked = HeaderVisible(header);
        _cbShortCode.IsChecked = _cfg.ShortCode;
        _cbFutAbbrev.IsChecked = _cfg.FutAbbrev;

        for (int i = 0; i < _cmbInterval.Items.Count; i++)
        {
            if (_cmbInterval.Items[i] is ComboBoxItem item && item.Tag is double value &&
                Math.Abs(value - _cfg.RefreshSeconds) < 0.01)
            {
                _cmbInterval.SelectedIndex = i;
                break;
            }
        }
        for (int i = 0; i < _cmbNameLength.Items.Count; i++)
        {
            if (_cmbNameLength.Items[i] is ComboBoxItem item && item.Tag is int value && value == _cfg.NameLength)
            {
                _cmbNameLength.SelectedIndex = i;
                break;
            }
        }
        for (int i = 0; i < _cmbB1s1Display.Items.Count; i++)
        {
            if (_cmbB1s1Display.Items[i] is ComboBoxItem item && item.Tag?.ToString() == _cfg.B1s1Display)
            {
                _cmbB1s1Display.SelectedIndex = i;
                break;
            }
        }
        _suppressChange = false;
        UpdateMarketSensitiveControls();
    }

    private void UpdateAbbrevEnabled()
    {
        _cbFutAbbrev.IsEnabled = _cfg.Market == "futures" && _cfg.NameVisible;
    }

    private void UpdateMarketSensitiveControls()
    {
        UpdateAbbrevEnabled();
        _cbShortCode.IsEnabled = _cfg.CodeVisible;
        _cmbNameLength.IsEnabled = _cfg.NameVisible;
        foreach (var (header, cb) in _colCheckboxes)
            cb.IsEnabled = _cfg.Market != "futures" || !Constants.FuturesUnsupported.Contains(header);
        _cmbB1s1Display.IsEnabled = _cfg.Market == "stock" && _cfg.B1s1Visible;
    }

    // ====================== Tab2：外观 ======================

    private void BuildTabAppearance()
    {
        var tab = new TabItem { Header = "外观" };
        var panel = new StackPanel { Margin = new Thickness(10) };

        // 表格外观
        var gTable = new GroupBox { Header = "表格外观", Margin = new Thickness(0, 0, 0, 8) };
        var tablePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
        _chkTableHeader.Content = "显示表头";
        _chkTableHeader.IsChecked = _cfg.HeaderVisible;
        _chkTableHeader.Checked += (s, e) => { _cfg.HeaderVisible = true; NotifySave(); };
        _chkTableHeader.Unchecked += (s, e) => { _cfg.HeaderVisible = false; NotifySave(); };
        _chkTableGrid.Content = "显示网格";
        _chkTableGrid.IsChecked = _cfg.GridVisible;
        _chkTableGrid.Checked += (s, e) => { _cfg.GridVisible = true; _chkColumnResize.IsEnabled = true; NotifySave(); };
        _chkTableGrid.Unchecked += (s, e) => { _cfg.GridVisible = false; _chkColumnResize.IsEnabled = false; NotifySave(); };
        _chkTableGrid.Margin = new Thickness(16, 0, 0, 0);
        _chkColumnResize.Content = "允许拖动自定义网格边框";
        _chkColumnResize.IsChecked = _cfg.ColumnResizeEnabled;
        _chkColumnResize.IsEnabled = _cfg.GridVisible;
        _chkColumnResize.Margin = new Thickness(16, 0, 0, 0);
        _chkColumnResize.Checked += (s, e) => { _cfg.ColumnResizeEnabled = true; NotifySave(); };
        _chkColumnResize.Unchecked += (s, e) => { _cfg.ColumnResizeEnabled = false; NotifySave(); };
        tablePanel.Children.Add(_chkTableHeader);
        tablePanel.Children.Add(_chkTableGrid);
        tablePanel.Children.Add(_chkColumnResize);
        gTable.Content = tablePanel;
        panel.Children.Add(gTable);

        // 颜色与透明度
        var gColor = new GroupBox { Header = "颜色与透明度", Margin = new Thickness(0, 0, 0, 8) };
        var colorGrid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
        for (int i = 0; i < 6; i++) colorGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 5; i++) colorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _chkDefaultColor.Content = "默认颜色";
        _chkDefaultColor.IsChecked = _cfg.DefaultColor;
        _chkDefaultColor.Checked += (s, e) => { _cfg.DefaultColor = true; NotifySave(); };
        _chkDefaultColor.Unchecked += (s, e) => { _cfg.DefaultColor = false; NotifySave(); };
        Grid.SetRow(_chkDefaultColor, 0); Grid.SetColumn(_chkDefaultColor, 0); Grid.SetColumnSpan(_chkDefaultColor, 6);

        var btnFg = new Button { Content = "文字颜色…", Width = 90, Margin = new Thickness(4), IsEnabled = !_cfg.DefaultColor };
        btnFg.Click += (s, e) =>
        {
            var c = PickColor(_cfg.Fg);
            if (c != null) ApplyFgColor(c);
        };
        colorGrid.Children.Add(new TextBlock { Text = "文字颜色：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        Grid.SetRow(colorGrid.Children[^1], 1); Grid.SetColumn(colorGrid.Children[^1], 0); Grid.SetColumnSpan(colorGrid.Children[^1], 2);
        Grid.SetRow(btnFg, 1); Grid.SetColumn(btnFg, 2); Grid.SetColumnSpan(btnFg, 2);
        _chkDefaultColor.Checked += (s, e) => btnFg.IsEnabled = false;
        _chkDefaultColor.Unchecked += (s, e) => btnFg.IsEnabled = true;

        var btnBg = new Button { Content = "背景颜色…", Width = 90, Margin = new Thickness(4) };
        btnBg.Click += (s, e) =>
        {
            var hex = $"#{_cfg.Bg.R:X2}{_cfg.Bg.G:X2}{_cfg.Bg.B:X2}";
            var c = PickColor(hex);
            if (c != null) ApplyBgColor(c);
        };
        colorGrid.Children.Add(new TextBlock { Text = "背景颜色：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        Grid.SetRow(colorGrid.Children[^1], 2); Grid.SetColumn(colorGrid.Children[^1], 0); Grid.SetColumnSpan(colorGrid.Children[^1], 2);
        Grid.SetRow(btnBg, 2); Grid.SetColumn(btnBg, 2); Grid.SetColumnSpan(btnBg, 2);

        colorGrid.Children.Add(_chkDefaultColor);
        colorGrid.Children.Add(btnFg);
        colorGrid.Children.Add(btnBg);
        AddSliderRow(colorGrid, 3, "背景不透明度：", _sliderBgAlpha, _lblBgAlpha, 1, 100, (int)Math.Round(_cfg.Bg.A / 2.55),
            v => { _cfg.Bg.A = (int)Math.Round(v * 2.55); MarkDirty(); });
        AddSliderRow(colorGrid, 4, "整体不透明度：", _sliderWinOpacity, _lblWinOpacity, 20, 100, _cfg.OpacityPct,
            v => { _cfg.OpacityPct = v; MarkDirty(); });
        gColor.Content = colorGrid;
        panel.Children.Add(gColor);

        // 字体与行距
        var gFont = new GroupBox { Header = "字体与行距" };
        var fontGrid = new Grid { Margin = new Thickness(4) };
        for (int i = 0; i < 6; i++) fontGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 3; i++) fontGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        foreach (var fam in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
            _cmbFamily.Items.Add(fam.Source);
        _cmbFamily.SelectedItem = _cfg.FontFamily;
        _cmbFamily.Width = 260;
        _cmbFamily.SelectionChanged += (s, e) =>
        {
            if (_suppressChange) return;
            if (_cmbFamily.SelectedItem is string fam) { _cfg.FontFamily = fam; MarkDirty(); }
        };
        Grid.SetRow(_cmbFamily, 0); Grid.SetColumn(_cmbFamily, 2); Grid.SetColumnSpan(_cmbFamily, 4);
        fontGrid.Children.Add(new TextBlock { Text = "字体：", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(fontGrid.Children[0], 0); Grid.SetColumn(fontGrid.Children[0], 0); Grid.SetColumnSpan(fontGrid.Children[0], 2);
        fontGrid.Children.Add(_cmbFamily);

        AddSliderRow(fontGrid, 1, "字号：", _sliderFont, _lblFont, 6, 28, _cfg.FontSize,
            v => { _cfg.FontSize = v; MarkDirty(); }, " pt");
        AddSliderRow(fontGrid, 2, "行距：", _sliderLine, _lblLine, -10, 30, _cfg.LineExtraPx,
            v => { _cfg.LineExtraPx = v; MarkDirty(); }, " px");
        gFont.Content = fontGrid;
        panel.Children.Add(gFont);

        tab.Content = panel;
        Tabs.Items.Add(tab);
    }

    private static void AddSliderRow(Grid grid, int row, string label, Slider slider, TextBlock valueLabel, int min, int max, int value, Action<int> onChange, string suffix = "%")
    {
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 3, 6, 3) };
        Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0); Grid.SetColumnSpan(lbl, 2);
        slider.Minimum = min; slider.Maximum = max; slider.Value = value;
        slider.SmallChange = 1; slider.LargeChange = 1;
        slider.MinWidth = 150;
        slider.Margin = new Thickness(4, 3, 8, 3);
        valueLabel.Text = $"{value}{suffix}";
        valueLabel.VerticalAlignment = VerticalAlignment.Center;
        valueLabel.MinWidth = 44;
        slider.ValueChanged += (s, e) => { valueLabel.Text = $"{(int)slider.Value}{suffix}"; onChange((int)slider.Value); };
        Grid.SetRow(slider, row); Grid.SetColumn(slider, 2); Grid.SetColumnSpan(slider, 3);
        Grid.SetRow(valueLabel, row); Grid.SetColumn(valueLabel, 5);
        grid.Children.Add(lbl);
        grid.Children.Add(slider);
        grid.Children.Add(valueLabel);
    }

    private string? PickColor(string currentHex)
    {
        var selectedHex = currentHex;
        var preview = new Border
        {
            Width = 72,
            Height = 28,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0)
        };

        void UpdatePreview()
        {
            try
            {
                preview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(selectedHex));
            }
            catch
            {
                preview.Background = Brushes.Transparent;
            }
        }

        var picker = new Window
        {
            Title = "颜色",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false
        };

        var root = new StackPanel { Margin = new Thickness(12) };
        var previewRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var hexText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 78 };
        void SyncText()
        {
            UpdatePreview();
            hexText.Text = selectedHex;
        }
        SyncText();
        previewRow.Children.Add(preview);
        previewRow.Children.Add(hexText);

        var btnChoose = new Button { Content = "选择颜色…", Height = 28, Margin = new Thickness(0, 0, 0, 6) };
        btnChoose.Click += (s, e) =>
        {
            var dlg = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
            };
            try { dlg.Color = System.Drawing.ColorTranslator.FromHtml(selectedHex); } catch { }
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var c = dlg.Color;
                selectedHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                SyncText();
            }
        };

        var btnPickScreen = new Button { Content = "屏幕取色", Height = 28, Margin = new Thickness(0, 0, 0, 10) };
        btnPickScreen.Click += (s, e) =>
        {
            var oldOpacity = picker.Opacity;
            var oldEnabled = picker.IsEnabled;
            picker.Opacity = 0;
            picker.IsEnabled = false;
            try
            {
                var c = PickScreenColor();
                if (c != null)
                {
                    selectedHex = c;
                    SyncText();
                }
            }
            finally
            {
                picker.Opacity = oldOpacity;
                picker.IsEnabled = oldEnabled;
                picker.Activate();
            }
        };

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnOk = new Button { Content = "确认", Width = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        btnOk.Click += (s, e) => picker.DialogResult = true;
        var btnCancel = new Button { Content = "取消", Width = 72, IsCancel = true };
        actionRow.Children.Add(btnOk);
        actionRow.Children.Add(btnCancel);

        root.Children.Add(previewRow);
        root.Children.Add(btnChoose);
        root.Children.Add(btnPickScreen);
        root.Children.Add(actionRow);
        picker.Content = root;

        return picker.ShowDialog() == true ? selectedHex : null;
    }

    private void ApplyFgColor(string hex)
    {
        _cfg.Fg = hex;
        MarkDirty();
    }

    private void ApplyBgColor(string hex)
    {
        var col = (Color)ColorConverter.ConvertFromString(hex);
        _cfg.Bg.R = col.R;
        _cfg.Bg.G = col.G;
        _cfg.Bg.B = col.B;
        MarkDirty();
    }

    private string? PickScreenColor()
    {
        var pickedPoint = default(Point?);
        var overlay = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Topmost = true,
            ShowInTaskbar = false,
            Cursor = Cursors.Cross,
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)),
                Child = new TextBlock
                {
                    Text = "点击屏幕任意位置取色，按 Esc 取消",
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                    Padding = new Thickness(10, 6, 10, 6),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 20, 0, 0),
                }
            }
        };
        overlay.MouseDown += (s, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            pickedPoint = overlay.PointToScreen(e.GetPosition(overlay));
            overlay.DialogResult = true;
        };
        overlay.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                overlay.DialogResult = false;
            }
        };

        Hide();
        try
        {
            overlay.ShowDialog();
        }
        finally
        {
            Show();
            Activate();
        }
        if (pickedPoint is not { } pt) return null;

        // Give Windows a breath to repaint after the transparent picker window closes.
        Thread.Sleep(80);
        using var bmp = new System.Drawing.Bitmap(1, 1);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.CopyFromScreen((int)Math.Round(pt.X), (int)Math.Round(pt.Y), 0, 0, new System.Drawing.Size(1, 1));
        var c = bmp.GetPixel(0, 0);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    // ====================== Tab3：常规 ======================

    private void BuildTabGeneral()
    {
        var tab = new TabItem { Header = "常规" };
        var panel = new StackPanel { Margin = new Thickness(10) };

        _chkStartOnBoot.Content = "开机启动";
        _chkStartOnBoot.IsChecked = _cfg.StartOnBoot;
        _chkStartOnBoot.Checked += (s, e) => { _cfg.StartOnBoot = true; MarkDirty(); };
        _chkStartOnBoot.Unchecked += (s, e) => { _cfg.StartOnBoot = false; MarkDirty(); };
        var gStartup = new GroupBox { Header = "启动" };
        var startupPanel = new StackPanel { Margin = new Thickness(2) };
        startupPanel.Children.Add(_chkStartOnBoot);
        gStartup.Content = startupPanel;
        panel.Children.Add(gStartup);

        var gWindow = new GroupBox { Header = "窗口行为", Margin = new Thickness(0, 8, 0, 0) };
        var windowGrid = new Grid { Margin = new Thickness(4) };
        for (int i = 0; i < 6; i++) windowGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 3; i++) windowGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _chkSnapEnabled.Content = "启用贴边吸附";
        _chkSnapEnabled.IsChecked = _cfg.SnapEnabled;
        _chkSnapEnabled.Margin = new Thickness(4, 2, 4, 5);
        _chkSnapEnabled.Checked += (s, e) => { _cfg.SnapEnabled = true; _sliderSnapDistance.IsEnabled = true; MarkDirty(); };
        _chkSnapEnabled.Unchecked += (s, e) => { _cfg.SnapEnabled = false; _sliderSnapDistance.IsEnabled = false; MarkDirty(); };
        Grid.SetRow(_chkSnapEnabled, 0); Grid.SetColumn(_chkSnapEnabled, 0); Grid.SetColumnSpan(_chkSnapEnabled, 6);
        windowGrid.Children.Add(_chkSnapEnabled);
        AddSliderRow(windowGrid, 1, "吸附距离：", _sliderSnapDistance, _lblSnapDistance, 0, 80, _cfg.SnapDistance,
            v => { _cfg.SnapDistance = v; MarkDirty(); }, " px");
        _sliderSnapDistance.IsEnabled = _cfg.SnapEnabled;
        _chkMouseThrough.Content = "鼠标穿透";
        _chkMouseThrough.IsChecked = _cfg.MouseThroughEnabled;
        _chkMouseThrough.Margin = new Thickness(4, 5, 4, 2);
        _chkMouseThrough.ToolTip = "开启后悬浮窗不再接收鼠标点击，可从托盘菜单或设置页关闭。";
        _chkMouseThrough.Checked += (s, e) => { _cfg.MouseThroughEnabled = true; MarkDirty(); };
        _chkMouseThrough.Unchecked += (s, e) => { _cfg.MouseThroughEnabled = false; MarkDirty(); };
        Grid.SetRow(_chkMouseThrough, 2); Grid.SetColumn(_chkMouseThrough, 0); Grid.SetColumnSpan(_chkMouseThrough, 6);
        windowGrid.Children.Add(_chkMouseThrough);
        gWindow.Content = windowGrid;
        panel.Children.Add(gWindow);

        var gHotkey = new GroupBox { Header = "快捷键" };
        var hkPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
        hkPanel.Children.Add(new TextBlock { Text = "隐藏/显示浮窗：", VerticalAlignment = VerticalAlignment.Center });
        _txtHotkey.Width = 170; _txtHotkey.Margin = new Thickness(8, 0, 0, 0);
        _txtHotkey.Text = _cfg.Hotkey;
        _txtHotkey.IsReadOnly = false;
        _txtHotkey.IsUndoEnabled = false;
        _txtHotkey.ToolTip = "在此框内按下新的快捷键组合";
        _txtHotkey.PreviewKeyDown += OnHotkeyKeyDown;
        _txtHotkey.PreviewTextInput += (s, e) => e.Handled = true;
        _txtHotkey.GotKeyboardFocus += (s, e) => _txtHotkey.SelectAll();
        hkPanel.Children.Add(_txtHotkey);
        var clearHotkey = new Button
        {
            Content = "×",
            Width = 28,
            MinWidth = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "清空并禁用快捷键"
        };
        clearHotkey.Click += (s, e) =>
        {
            if (_setHotkeyCallback?.Invoke("") == true)
            {
                _cfg.Hotkey = "";
                _txtHotkey.Text = "";
                _hotkeyChanged = true;
                MarkDirty();
                _txtHotkey.Focus();
            }
        };
        hkPanel.Children.Add(clearHotkey);
        gHotkey.Content = hkPanel;
        panel.Children.Add(gHotkey);

        var gIcon = new GroupBox { Header = "程序图标" };
        var iconPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
        iconPanel.Children.Add(new TextBlock { Text = "当前图标：", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        _cmbIcon.Width = 170;
        _cmbIcon.Items.Add(new ComboBoxItem { Content = "默认", Tag = "default" });
        if (!string.IsNullOrEmpty(_cfg.AppIcon) && _cfg.AppIcon != "default" && File.Exists(_cfg.AppIcon))
            _cmbIcon.Items.Add(new ComboBoxItem { Content = "自定义", Tag = _cfg.AppIcon });
        _cmbIcon.SelectedIndex = _cfg.AppIcon != null && _cfg.AppIcon != "default" && File.Exists(_cfg.AppIcon) ? 1 : 0;
        _cmbIcon.SelectionChanged += (s, e) =>
        {
            if (_suppressChange) return;
            if (_cmbIcon.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString();
                _cfg.AppIcon = tag;
                MarkDirty();
            }
        };
        var btnPickIcon = new Button { Content = "自定义图标…", Margin = new Thickness(8, 0, 0, 0) };
        btnPickIcon.Click += (s, e) =>
        {
            var ofd = new OpenFileDialog { Filter = "图标文件 (*.ico)|*.ico|All Files (*.*)|*.*", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            if (ofd.ShowDialog() == true)
            {
                _cfg.AppIcon = ofd.FileName;
                if (_cmbIcon.Items.Cast<ComboBoxItem>().All(x => x.Tag?.ToString() != ofd.FileName))
                    _cmbIcon.Items.Add(new ComboBoxItem { Content = "自定义", Tag = ofd.FileName });
                _cmbIcon.SelectedItem = _cmbIcon.Items.Cast<ComboBoxItem>().First(x => x.Tag?.ToString() == ofd.FileName);
                MarkDirty();
            }
        };
        iconPanel.Children.Add(_cmbIcon);
        iconPanel.Children.Add(btnPickIcon);
        gIcon.Content = iconPanel;
        panel.Children.Add(gIcon);

        tab.Content = panel;
        Tabs.Items.Add(tab);
    }

    private static void ApplyStartOnBoot(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key != null)
            {
                if (enabled)
                {
                    string exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (!string.IsNullOrEmpty(exe)) key.SetValue("StockWidget", $"\"{exe}\"");
                }
                else key.DeleteValue("StockWidget", false);
            }
        }
        catch { }
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;
        var keyText = KeyToHotkeyText(key);
        if (keyText == null) return;

        var parts = new List<string>();
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(keyText);
        var hotkey = string.Join("+", parts);
        if (_setHotkeyCallback?.Invoke(hotkey) == true)
        {
            _cfg.Hotkey = hotkey;
            _txtHotkey.Text = hotkey;
            _hotkeyChanged = true;
            MarkDirty();
        }
        else
        {
            MessageBox.Show(this, "快捷键无法注册，已保留原设置。", "快捷键", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string? KeyToHotkeyText(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return key.ToString();
        if (key is >= Key.D0 and <= Key.D9) return ((int)(key - Key.D0)).ToString();
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return ((int)(key - Key.NumPad0)).ToString();
        if (key is >= Key.F1 and <= Key.F24) return key.ToString();
        return null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _accepted = true;
        ApplyDraftToLive();
        _saveCallback?.Invoke();
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_accepted) RestoreOriginal();
        if (SaveWindowPosition()) _saveCallback?.Invoke();
        base.OnClosing(e);
    }

    private void RestoreWindowPosition()
    {
        if (!_targetConfig.HasSettingsPos) return;

        var width = double.IsNaN(Width) || Width <= 0 ? 560 : Width;
        var height = double.IsNaN(Height) || Height <= 0 ? 540 : Height;
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var maxLeft = left + SystemParameters.VirtualScreenWidth - width;
        var maxTop = top + SystemParameters.VirtualScreenHeight - height;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Math.Clamp(_targetConfig.SettingsPosX, left, Math.Max(left, maxLeft));
        Top = Math.Clamp(_targetConfig.SettingsPosY, top, Math.Max(top, maxTop));
    }

    private bool SaveWindowPosition()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;
        if (!double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top)) return false;

        var changed = !_targetConfig.HasSettingsPos ||
                      Math.Abs(_targetConfig.SettingsPosX - bounds.Left) > 0.5 ||
                      Math.Abs(_targetConfig.SettingsPosY - bounds.Top) > 0.5;
        _targetConfig.SettingsPosX = bounds.Left;
        _targetConfig.SettingsPosY = bounds.Top;
        _targetConfig.HasSettingsPos = true;
        return changed;
    }

    private void MarkDirty()
    {
        if (!_suppressChange) ApplyDraftToLive();
    }

    private void NotifySave() => MarkDirty();

    private void ApplyDraftToLive()
    {
        CopySettingsPreservingPosition(_cfg);
        _win.ApplyConfigChanges();
        _setIconCallback?.Invoke(_targetConfig.AppIcon);
        ApplyStartOnBoot(_targetConfig.StartOnBoot);
    }

    private void RestoreOriginal()
    {
        CopySettingsPreservingPosition(_original);
        _win.ApplyConfigChanges();
        _setIconCallback?.Invoke(_targetConfig.AppIcon);
        if (_hotkeyChanged) _setHotkeyCallback?.Invoke(_targetConfig.Hotkey);
        ApplyStartOnBoot(_targetConfig.StartOnBoot);
    }

    private void CopySettingsPreservingPosition(AppConfig source)
    {
        var posX = _targetConfig.PosX;
        var posY = _targetConfig.PosY;
        var hasPos = _targetConfig.HasPos;
        var settingsPosX = _targetConfig.SettingsPosX;
        var settingsPosY = _targetConfig.SettingsPosY;
        var hasSettingsPos = _targetConfig.HasSettingsPos;
        _targetConfig.CopyFrom(source);
        _targetConfig.PosX = posX;
        _targetConfig.PosY = posY;
        _targetConfig.HasPos = hasPos;
        _targetConfig.SettingsPosX = settingsPosX;
        _targetConfig.SettingsPosY = settingsPosY;
        _targetConfig.HasSettingsPos = hasSettingsPos;
    }
}
