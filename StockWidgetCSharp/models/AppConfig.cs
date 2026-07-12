using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StockWidget;

/// <summary>
/// 应用配置（强类型视图），封装 Python current_config 的全部键。
/// 负责从 JsonObject 加载（含旧格式迁移）和回写。
/// </summary>
internal sealed class AppConfig
{
    // 市场
    public string Market { get; set; } = "stock";

    // 两套独立自选存储
    public List<string> StockCodes { get; set; } = new() { "sh000001" };
    public List<string> StockChecked { get; set; } = new() { "sh000001" };
    public List<string> FutCodes { get; set; } = new() { "nf_RB2610" };
    public List<string> FutChecked { get; set; } = new() { "nf_RB2610" };

    // 当前市场视图
    public List<string> Codes => Market == "stock" ? StockCodes : FutCodes;
    public List<string> CheckedCodes => Market == "stock" ? StockChecked : FutChecked;
    public Dictionary<string, PositionInfo> Positions => Market == "stock" ? StockPositions : FutPositions;

    // 持仓
    public Dictionary<string, PositionInfo> StockPositions { get; set; } = new();
    public Dictionary<string, PositionInfo> FutPositions { get; set; } = new();
    public Dictionary<string, FuturesProductParameter> FutProductParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetFuturesProductParameter(string product, out FuturesProductParameter parameter)
    {
        if (FutProductParameters.TryGetValue(product, out var custom) && custom.IsValid)
        {
            parameter = custom;
            return true;
        }
        if (FuturesProductCatalog.TryGet(product, out var spec))
        {
            parameter = new FuturesProductParameter { TickSize = spec.TickSize, TickProfit = spec.TickProfit };
            return true;
        }
        parameter = null!;
        return false;
    }

    // 股票和期货分别保存显示数据配置，当前市场通过下列属性访问。
    public MarketDisplayConfig StockDisplayData { get; set; } = new();
    public MarketDisplayConfig FutDisplayData { get; set; } = new();
    private MarketDisplayConfig DisplayData => Market == "stock" ? StockDisplayData : FutDisplayData;

    public bool PositionSummaryVisible { get => DisplayData.PositionSummaryVisible; set => DisplayData.PositionSummaryVisible = value; }

    // 显示
    public bool ShortCode { get => DisplayData.ShortCode; set => DisplayData.ShortCode = value; }
    public int NameLength { get => DisplayData.NameLength; set => DisplayData.NameLength = value; }
    public bool FutAbbrev { get => DisplayData.FutAbbrev; set => DisplayData.FutAbbrev = value; }
    public string B1s1Display { get => DisplayData.B1s1Display; set => DisplayData.B1s1Display = value; }   // qty/price/both

    // 表格外观
    public bool HeaderVisible { get; set; }
    public bool GridVisible { get; set; }
    public bool ColumnResizeEnabled { get; set; }
    public Dictionary<string, int> ColWidths { get; set; } = new();

    // 列可见性
    public bool CodeVisible { get => DisplayData.CodeVisible; set => DisplayData.CodeVisible = value; }
    public bool NameVisible { get => DisplayData.NameVisible; set => DisplayData.NameVisible = value; }
    public bool PriceVisible { get => DisplayData.PriceVisible; set => DisplayData.PriceVisible = value; }
    public bool ChangeVisible { get => DisplayData.ChangeVisible; set => DisplayData.ChangeVisible = value; }
    public bool ChangePctVisible { get => DisplayData.ChangePctVisible; set => DisplayData.ChangePctVisible = value; }
    public bool B1s1Visible { get => DisplayData.B1s1Visible; set => DisplayData.B1s1Visible = value; }
    public bool CommiVisible { get => DisplayData.CommiVisible; set => DisplayData.CommiVisible = value; }
    public bool VolVisible { get => DisplayData.VolVisible; set => DisplayData.VolVisible = value; }
    public bool AmountVisible { get => DisplayData.AmountVisible; set => DisplayData.AmountVisible = value; }
    public bool AvgVisible { get => DisplayData.AvgVisible; set => DisplayData.AvgVisible = value; }
    public bool KlineVisible { get => DisplayData.KlineVisible; set => DisplayData.KlineVisible = value; }
    public bool PositionCostVisible { get => DisplayData.PositionCostVisible; set => DisplayData.PositionCostVisible = value; }
    public bool PositionQuantityVisible { get => DisplayData.PositionQuantityVisible; set => DisplayData.PositionQuantityVisible = value; }
    public bool PositionProfitVisible { get => DisplayData.PositionProfitVisible; set => DisplayData.PositionProfitVisible = value; }
    public bool PositionProfitPctVisible { get => DisplayData.PositionProfitPctVisible; set => DisplayData.PositionProfitPctVisible = value; }

    // 刷新
    public double RefreshSeconds { get => DisplayData.RefreshSeconds; set => DisplayData.RefreshSeconds = value; }

    // 颜色/字体
    public string Fg { get; set; } = "#FFFFFF";
    public BgColor Bg { get; set; } = new();
    public int OpacityPct { get; set; } = 90;
    public string FontFamily { get; set; } = "Microsoft YaHei";
    public int FontSize { get; set; } = 10;
    public int LineExtraPx { get; set; } = 1;
    public bool DefaultColor { get; set; }
    public bool SnapEnabled { get; set; } = true;
    public int SnapDistance { get; set; } = 18;
    public bool MouseThroughEnabled { get; set; }

    // 位置/热键/自启
    public double PosX { get; set; } = -1;
    public double PosY { get; set; } = -1;
    public bool HasPos { get; set; }
    public double SettingsPosX { get; set; }
    public double SettingsPosY { get; set; }
    public bool HasSettingsPos { get; set; }
    public string Hotkey { get; set; } = "Ctrl+Alt+F";
    public bool StartOnBoot { get; set; }

    // 图标
    public string? AppIcon { get; set; }

    public AppConfig Clone() => FromJson(ToJson());

    public void CopyFrom(AppConfig other)
    {
        Market = other.Market;
        StockCodes = other.StockCodes.ToList();
        StockChecked = other.StockChecked.ToList();
        FutCodes = other.FutCodes.ToList();
        FutChecked = other.FutChecked.ToList();
        StockPositions = other.StockPositions.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());
        FutPositions = other.FutPositions.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());
        FutProductParameters = other.FutProductParameters.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
        StockDisplayData = other.StockDisplayData.Clone();
        FutDisplayData = other.FutDisplayData.Clone();
        HeaderVisible = other.HeaderVisible;
        GridVisible = other.GridVisible;
        ColumnResizeEnabled = other.ColumnResizeEnabled;
        ColWidths = other.ColWidths.ToDictionary(kv => kv.Key, kv => kv.Value);
        Fg = other.Fg;
        Bg = new BgColor { R = other.Bg.R, G = other.Bg.G, B = other.Bg.B, A = other.Bg.A };
        OpacityPct = other.OpacityPct;
        FontFamily = other.FontFamily;
        FontSize = other.FontSize;
        LineExtraPx = other.LineExtraPx;
        DefaultColor = other.DefaultColor;
        SnapEnabled = other.SnapEnabled;
        SnapDistance = other.SnapDistance;
        MouseThroughEnabled = other.MouseThroughEnabled;
        PosX = other.PosX;
        PosY = other.PosY;
        HasPos = other.HasPos;
        SettingsPosX = other.SettingsPosX;
        SettingsPosY = other.SettingsPosY;
        HasSettingsPos = other.HasSettingsPos;
        Hotkey = other.Hotkey;
        StartOnBoot = other.StartOnBoot;
        AppIcon = other.AppIcon;
    }

    /// <summary>从 JsonObject 加载（含旧格式迁移）。</summary>
    public static AppConfig FromJson(JsonObject o)
    {
        var c = new AppConfig();

        // 市场
        c.Market = Config.GetStr(o, "market", "stock");
        if (c.Market != "stock" && c.Market != "futures") c.Market = "stock";

        // 自选：两套独立存储，兼容旧单组 codes/checked_codes
        bool HasArray(string key) => o.TryGetPropertyValue(key, out var node) && node is JsonArray;

        var legacyCodes = HasArray("codes")
            ? Config.GetStrList(o, "codes")
            : new List<string> { "sh000001" };
        var hasLegacyChecked = HasArray("checked_codes") || HasArray("visible_codes");
        var legacyCheckedRaw = HasArray("checked_codes")
            ? Config.GetStrList(o, "checked_codes")
            : HasArray("visible_codes")
                ? Config.GetStrList(o, "visible_codes")
                : legacyCodes.ToList();

        c.StockCodes = HasArray("stock_codes")
            ? Config.GetStrList(o, "stock_codes")
            : c.Market == "stock" ? legacyCodes.ToList() : new List<string> { "sh000001" };
        c.StockChecked = HasArray("stock_checked")
            ? Config.GetStrList(o, "stock_checked")
            : c.Market == "stock" && hasLegacyChecked ? legacyCheckedRaw.ToList() : c.StockCodes.ToList();

        c.FutCodes = HasArray("fut_codes")
            ? Config.GetStrList(o, "fut_codes")
            : c.Market == "futures" ? legacyCodes.ToList() : new List<string> { "nf_RB2610" };
        c.FutChecked = HasArray("fut_checked")
            ? Config.GetStrList(o, "fut_checked")
            : c.Market == "futures" && hasLegacyChecked ? legacyCheckedRaw.ToList() : c.FutCodes.ToList();

        // 各组按市场过滤归一化
        c.StockCodes = FilterByMarket(c.StockCodes, "stock");
        c.StockChecked = FilterByMarket(c.StockChecked, "stock").Where(x => c.StockCodes.Contains(x)).ToList();
        c.FutCodes = FilterByMarket(c.FutCodes, "futures");
        c.FutChecked = FilterByMarket(c.FutChecked, "futures").Where(x => c.FutCodes.Contains(x)).ToList();
        c.StockPositions = FilterPositions(ParsePositions(o, "stock_positions"), c.StockCodes);
        c.FutPositions = FilterPositions(ParsePositions(o, "fut_positions"), c.FutCodes);
        c.FutProductParameters = ParseFuturesProductParameters(o);
        foreach (var stockPosition in c.StockPositions.Values) stockPosition.Direction = "long";
        c.PositionSummaryVisible = Config.GetBool(o, "position_summary_visible", true);

        // 显示
        c.ShortCode = Config.GetBool(o, "short_code", false);
        c.NameLength = Config.GetInt(o, "name_length", 0);
        c.FutAbbrev = Config.GetBool(o, "fut_abbrev", false);
        // b1s1_display：优先新键，回退旧 b1s1_price(bool)
        var disp = Config.GetStr(o, "b1s1_display", "");
        if (disp == "qty" || disp == "price" || disp == "both") c.B1s1Display = disp;
        else c.B1s1Display = Config.GetBool(o, "b1s1_price", false) ? "price" : "qty";

        // 表格外观
        c.HeaderVisible = Config.GetBool(o, "header_visible", false);
        c.GridVisible = Config.GetBool(o, "grid_visible", false);
        c.ColumnResizeEnabled = Config.GetBool(o, "column_resize_enabled", false);
        c.ColWidths = Config.GetColWidths(o, "col_widths");

        // 列可见性：解析旧 flags（list/dict）做回退
        var oldFlags = ParseOldFlags(o, Constants.AllHeaders);

        bool ColVis(string header, string newKey)
        {
            if (o.TryGetPropertyValue(newKey, out _)) return Config.GetBool(o, newKey, false);
            return oldFlags.TryGetValue(header, out var b) && b;
        }
        c.CodeVisible = ColVis("代码", "code_visible");
        c.NameVisible = ColVis("名称", "name_visible");
        c.PriceVisible = ColVis("现价", "price_visible");
        c.ChangeVisible = ColVis("涨跌值", "change_visible");
        c.ChangePctVisible = ColVis("涨跌幅", "change_pct_visible");
        // 买一/卖一：新键优先，否则两者任一为真
        if (o.TryGetPropertyValue("b1s1_visible", out _))
            c.B1s1Visible = Config.GetBool(o, "b1s1_visible", false);
        else
            c.B1s1Visible = (oldFlags.TryGetValue("买一", out var b1) && b1) || (oldFlags.TryGetValue("卖一", out var b2) && b2);
        c.CommiVisible = ColVis("委比", "commi_visible");
        c.VolVisible = ColVis("成交量", "vol_visible");
        c.AmountVisible = ColVis("成交额", "amount_visible");
        c.AvgVisible = ColVis("均价", "avg_visible");
        c.KlineVisible = ColVis("K线", "kline_visible");
        c.PositionCostVisible = ColVis("持仓成本", "position_cost_visible");
        c.PositionQuantityVisible = ColVis("持仓数量", "position_quantity_visible");
        c.PositionProfitVisible = ColVis("持仓盈亏", "position_profit_visible");
        c.PositionProfitPctVisible = ColVis("持仓盈亏率", "position_profit_pct_visible");

        // 刷新
        c.RefreshSeconds = Config.GetDouble(o, "refresh_seconds", 0.2);

        // 旧版只有一套显示数据：首次迁移时复制给股票和期货，再读取各市场的新配置覆盖。
        var legacyDisplay = c.DisplayData.Clone();
        c.StockDisplayData = ParseMarketDisplay(o, "stock_display", legacyDisplay);
        c.FutDisplayData = ParseMarketDisplay(o, "fut_display", legacyDisplay);

        // 颜色/字体
        c.Fg = Config.GetStr(o, "fg", "#FFFFFF");
        c.Bg = ParseBg(o);
        c.OpacityPct = Config.GetInt(o, "opacity_pct", 90);
        c.FontFamily = Config.GetStr(o, "font_family", "Microsoft YaHei");
        c.FontSize = Math.Max(6, Math.Min(28, Config.GetInt(o, "font_size", 10)));
        c.LineExtraPx = Math.Max(-10, Math.Min(30, Config.GetInt(o, "line_extra_px", 1)));
        c.DefaultColor = Config.GetBool(o, "default_color", false);
        c.SnapEnabled = Config.GetBool(o, "snap_enabled", true);
        c.SnapDistance = Math.Max(0, Math.Min(80, Config.GetInt(o, "snap_distance", 18)));
        c.MouseThroughEnabled = Config.GetBool(o, "mouse_through_enabled", false);

        // 位置
        if (o.TryGetPropertyValue("pos", out var posNode) && posNode is JsonObject pos)
        {
            c.PosX = Config.GetDouble(pos, "x", -1);
            c.PosY = Config.GetDouble(pos, "y", -1);
            var legacyHasPos = c.PosX != -1 || c.PosY != -1;
            c.HasPos = double.IsFinite(c.PosX) &&
                       double.IsFinite(c.PosY) &&
                       Config.GetBool(o, "has_pos", legacyHasPos);
        }

        if (o.TryGetPropertyValue("settings_pos", out var settingsPosNode) && settingsPosNode is JsonObject settingsPos)
        {
            c.SettingsPosX = Config.GetDouble(settingsPos, "x", 0);
            c.SettingsPosY = Config.GetDouble(settingsPos, "y", 0);
            c.HasSettingsPos = double.IsFinite(c.SettingsPosX) && double.IsFinite(c.SettingsPosY);
        }

        c.Hotkey = o.TryGetPropertyValue("hotkey", out var hotkeyNode) && hotkeyNode != null
            ? hotkeyNode.ToString().Trim()
            : "Ctrl+Alt+F";
        c.StartOnBoot = Config.GetBool(o, "start_on_boot", false);
        c.AppIcon = o.TryGetPropertyValue("app_icon", out var ic) && ic != null ? ic.ToString() : null;

        return c;
    }

    /// <summary>回写为 JsonObject（对应 Python current_config）。</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject();
        o["market"] = Market;
        o["stock_codes"] = Arr(StockCodes);
        o["stock_checked"] = Arr(StockChecked);
        o["fut_codes"] = Arr(FutCodes);
        o["fut_checked"] = Arr(FutChecked);
        o["stock_positions"] = PositionsObject(StockPositions);
        o["fut_positions"] = PositionsObject(FutPositions);
        o["fut_product_parameters"] = FuturesProductParametersObject(FutProductParameters);
        o["stock_display"] = DisplayObject(StockDisplayData);
        o["fut_display"] = DisplayObject(FutDisplayData);
        o["position_summary_visible"] = PositionSummaryVisible;
        o["codes"] = Arr(Codes);
        o["checked_codes"] = Arr(CheckedCodes);
        o["short_code"] = ShortCode;
        o["name_length"] = NameLength;
        o["fut_abbrev"] = FutAbbrev;
        o["b1s1_price"] = B1s1Display == "price";
        o["b1s1_display"] = B1s1Display;
        o["header_visible"] = HeaderVisible;
        o["grid_visible"] = GridVisible;
        o["column_resize_enabled"] = ColumnResizeEnabled;
        var cw = new JsonObject();
        foreach (var kv in ColWidths) cw[kv.Key] = kv.Value;
        o["col_widths"] = cw;
        o["refresh_seconds"] = RefreshSeconds;
        o["fg"] = Fg;
        var bg = new JsonObject { ["r"] = Bg.R, ["g"] = Bg.G, ["b"] = Bg.B, ["a"] = Bg.A };
        o["bg"] = bg;
        o["opacity_pct"] = OpacityPct;
        o["font_family"] = FontFamily;
        o["font_size"] = FontSize;
        o["line_extra_px"] = LineExtraPx;
        o["default_color"] = DefaultColor;
        o["snap_enabled"] = SnapEnabled;
        o["snap_distance"] = SnapDistance;
        o["mouse_through_enabled"] = MouseThroughEnabled;
        o["pos"] = new JsonObject { ["x"] = PosX, ["y"] = PosY };
        o["has_pos"] = HasPos;
        if (HasSettingsPos)
            o["settings_pos"] = new JsonObject { ["x"] = SettingsPosX, ["y"] = SettingsPosY };
        o["hotkey"] = Hotkey;
        o["start_on_boot"] = StartOnBoot;
        // 列可见性
        o["code_visible"] = CodeVisible;
        o["name_visible"] = NameVisible;
        o["price_visible"] = PriceVisible;
        o["change_visible"] = ChangeVisible;
        o["change_pct_visible"] = ChangePctVisible;
        o["b1s1_visible"] = B1s1Visible;
        o["commi_visible"] = CommiVisible;
        o["vol_visible"] = VolVisible;
        o["amount_visible"] = AmountVisible;
        o["avg_visible"] = AvgVisible;
        o["kline_visible"] = KlineVisible;
        o["position_cost_visible"] = PositionCostVisible;
        o["position_quantity_visible"] = PositionQuantityVisible;
        o["position_profit_visible"] = PositionProfitVisible;
        o["position_profit_pct_visible"] = PositionProfitPctVisible;
        if (AppIcon != null) o["app_icon"] = AppIcon;
        return o;
    }

    private static JsonArray Arr(List<string> list)
    {
        var arr = new JsonArray();
        foreach (var x in list) arr.Add(x);
        return arr;
    }

    private static JsonObject PositionsObject(Dictionary<string, PositionInfo> positions)
    {
        var obj = new JsonObject();
        foreach (var (code, pos) in positions)
        {
            if (!pos.IsValid) continue;
            obj[code] = new JsonObject
            {
                ["cost_price"] = pos.CostPrice,
                ["quantity"] = pos.Quantity,
                ["direction"] = pos.IsShort ? "short" : "long"
            };
        }
        return obj;
    }

    private static JsonObject FuturesProductParametersObject(
        Dictionary<string, FuturesProductParameter> parameters)
    {
        var obj = new JsonObject();
        foreach (var (product, parameter) in parameters.OrderBy(kv => kv.Key))
        {
            if (!parameter.IsValid) continue;
            obj[product.ToUpperInvariant()] = new JsonObject
            {
                ["tick_size"] = parameter.TickSize,
                ["tick_profit"] = parameter.TickProfit
            };
        }
        return obj;
    }

    private static Dictionary<string, FuturesProductParameter> ParseFuturesProductParameters(JsonObject root)
    {
        var result = new Dictionary<string, FuturesProductParameter>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetPropertyValue("fut_product_parameters", out var node) || node is not JsonObject obj)
            return result;

        foreach (var (rawProduct, value) in obj)
        {
            var product = FuturesProductCode.FromContract(rawProduct);
            if (product == null || value is not JsonObject parameterNode) continue;
            var parameter = new FuturesProductParameter
            {
                TickSize = Config.GetDouble(parameterNode, "tick_size", 1),
                TickProfit = Config.GetDouble(parameterNode, "tick_profit", 0)
            };
            if (parameter.IsValid) result[product] = parameter;
        }
        return result;
    }

    private static JsonObject DisplayObject(MarketDisplayConfig display)
    {
        return new JsonObject
        {
            ["position_summary_visible"] = display.PositionSummaryVisible,
            ["short_code"] = display.ShortCode,
            ["name_length"] = display.NameLength,
            ["fut_abbrev"] = display.FutAbbrev,
            ["b1s1_display"] = display.B1s1Display,
            ["code_visible"] = display.CodeVisible,
            ["name_visible"] = display.NameVisible,
            ["price_visible"] = display.PriceVisible,
            ["change_visible"] = display.ChangeVisible,
            ["change_pct_visible"] = display.ChangePctVisible,
            ["b1s1_visible"] = display.B1s1Visible,
            ["commi_visible"] = display.CommiVisible,
            ["vol_visible"] = display.VolVisible,
            ["amount_visible"] = display.AmountVisible,
            ["avg_visible"] = display.AvgVisible,
            ["kline_visible"] = display.KlineVisible,
            ["position_cost_visible"] = display.PositionCostVisible,
            ["position_quantity_visible"] = display.PositionQuantityVisible,
            ["position_profit_visible"] = display.PositionProfitVisible,
            ["position_profit_pct_visible"] = display.PositionProfitPctVisible,
            ["refresh_seconds"] = display.RefreshSeconds,
        };
    }

    private static MarketDisplayConfig ParseMarketDisplay(JsonObject root, string key, MarketDisplayConfig fallback)
    {
        var display = fallback.Clone();
        if (!root.TryGetPropertyValue(key, out var node) || node is not JsonObject o) return display;

        display.PositionSummaryVisible = Config.GetBool(o, "position_summary_visible", display.PositionSummaryVisible);
        display.ShortCode = Config.GetBool(o, "short_code", display.ShortCode);
        display.NameLength = Math.Max(0, Math.Min(4, Config.GetInt(o, "name_length", display.NameLength)));
        display.FutAbbrev = Config.GetBool(o, "fut_abbrev", display.FutAbbrev);
        var b1s1 = Config.GetStr(o, "b1s1_display", display.B1s1Display);
        display.B1s1Display = b1s1 is "qty" or "price" or "both" ? b1s1 : "qty";
        display.CodeVisible = Config.GetBool(o, "code_visible", display.CodeVisible);
        display.NameVisible = Config.GetBool(o, "name_visible", display.NameVisible);
        display.PriceVisible = Config.GetBool(o, "price_visible", display.PriceVisible);
        display.ChangeVisible = Config.GetBool(o, "change_visible", display.ChangeVisible);
        display.ChangePctVisible = Config.GetBool(o, "change_pct_visible", display.ChangePctVisible);
        display.B1s1Visible = Config.GetBool(o, "b1s1_visible", display.B1s1Visible);
        display.CommiVisible = Config.GetBool(o, "commi_visible", display.CommiVisible);
        display.VolVisible = Config.GetBool(o, "vol_visible", display.VolVisible);
        display.AmountVisible = Config.GetBool(o, "amount_visible", display.AmountVisible);
        display.AvgVisible = Config.GetBool(o, "avg_visible", display.AvgVisible);
        display.KlineVisible = Config.GetBool(o, "kline_visible", display.KlineVisible);
        display.PositionCostVisible = Config.GetBool(o, "position_cost_visible", display.PositionCostVisible);
        display.PositionQuantityVisible = Config.GetBool(o, "position_quantity_visible", display.PositionQuantityVisible);
        display.PositionProfitVisible = Config.GetBool(o, "position_profit_visible", display.PositionProfitVisible);
        display.PositionProfitPctVisible = Config.GetBool(o, "position_profit_pct_visible", display.PositionProfitPctVisible);
        display.RefreshSeconds = Config.GetDouble(o, "refresh_seconds", display.RefreshSeconds);
        return display;
    }

    private static Dictionary<string, PositionInfo> ParsePositions(JsonObject o, string key)
    {
        var result = new Dictionary<string, PositionInfo>();
        if (!o.TryGetPropertyValue(key, out var node) || node is not JsonObject obj) return result;
        foreach (var (code, val) in obj)
        {
            if (string.IsNullOrWhiteSpace(code) || val is not JsonObject p) continue;
            var pos = new PositionInfo
            {
                CostPrice = Config.GetDouble(p, "cost_price", 0),
                Quantity = Config.GetDouble(p, "quantity", 0),
                Direction = Config.GetStr(p, "direction", "long").Equals("short", StringComparison.OrdinalIgnoreCase)
                    ? "short"
                    : "long"
            };
            if (pos.IsValid) result[code] = pos;
        }
        return result;
    }

    private static Dictionary<string, PositionInfo> FilterPositions(Dictionary<string, PositionInfo> positions, List<string> codes)
    {
        var allowed = codes.ToHashSet();
        return positions
            .Where(kv => allowed.Contains(kv.Key) && kv.Value.IsValid)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Clone());
    }

    private static BgColor ParseBg(JsonObject o)
    {
        if (o.TryGetPropertyValue("bg", out var bgNode) && bgNode is JsonObject bg)
            return new() { R = Config.GetInt(bg, "r", 0), G = Config.GetInt(bg, "g", 0), B = Config.GetInt(bg, "b", 0), A = Config.GetInt(bg, "a", 191) };
        return new();
    }

    /// <summary>解析旧 flags 键（list 按 ALL_HEADERS 顺序，或 dict 按列标题）。</summary>
    private static Dictionary<string, bool> ParseOldFlags(JsonObject o, string[] headers)
    {
        var result = new Dictionary<string, bool>();
        if (!o.TryGetPropertyValue("flags", out var f) || f is null) return result;
        if (f is JsonArray arr)
        {
            for (int i = 0; i < headers.Length; i++)
                result[headers[i]] = i < arr.Count && arr[i] is JsonValue jv && jv.TryGetValue<bool>(out var b) && b;
        }
        else if (f is JsonObject obj)
        {
            foreach (var h in headers)
                result[h] = obj.TryGetPropertyValue(h, out var v) && v is JsonValue jv && jv.TryGetValue<bool>(out var b) && b;
        }
        return result;
    }

    /// <summary>
    /// 按市场过滤代码格式（对应 Python _filter_codes_by_market）。
    /// 期货采用宽容策略：只要 nf_ 前缀+至少一个字母数字就保留，避免误杀。
    /// </summary>
    public static List<string> FilterByMarket(List<string> codes, string market)
    {
        var outList = new List<string>();
        var seen = new HashSet<string>();
        foreach (var raw in codes)
        {
            var c = raw.Trim();
            if (string.IsNullOrEmpty(c) || seen.Contains(c)) continue;
            if (market == "futures")
            {
                if (!c.ToLowerInvariant().StartsWith("nf_") || c.Length <= 3) continue;
                var core = c[3..];
                if (string.IsNullOrEmpty(core) || !core.Any(ch => char.IsLetterOrDigit(ch))) continue;
                c = "nf_" + core.ToUpperInvariant();
            }
            else
            {
                if (!Constants.ReStock.IsMatch(c)) continue;
            }
            seen.Add(c);
            outList.Add(c);
        }
        return outList;
    }
}
