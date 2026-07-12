using System.Text.RegularExpressions;

namespace StockWidget;

/// <summary>
/// 全局常量：列标题、可见性映射、颜色、代码正则。
/// 对应 Python 版 _HEADER_ATTRS / _FUTURES_UNSUPPORTED / 颜色常量 / 正则。
/// </summary>
internal static class Constants
{
    // 列标题顺序（与 Python ALL_HEADERS 完全一致）
    public static readonly string[] AllHeaders =
    {
        "代码", "名称", "现价", "涨跌值", "涨跌幅", "买一", "卖一", "委比", "成交量", "成交额", "均价", "K线",
        "持仓成本", "持仓数量", "持仓盈亏", "持仓盈亏率"
    };

    // 列标题 -> 可见性属性名（买一/卖一共用 b1s1_visible）
    public static readonly Dictionary<string, string> HeaderAttrs = new()
    {
        { "代码", "code_visible" },
        { "名称", "name_visible" },
        { "现价", "price_visible" },
        { "涨跌值", "change_visible" },
        { "涨跌幅", "change_pct_visible" },
        { "买一", "b1s1_visible" },
        { "卖一", "b1s1_visible" },
        { "委比", "commi_visible" },
        { "成交量", "vol_visible" },
        { "成交额", "amount_visible" },
        { "均价", "avg_visible" },
        { "K线", "kline_visible" },
        { "持仓成本", "position_cost_visible" },
        { "持仓数量", "position_quantity_visible" },
        { "持仓盈亏", "position_profit_visible" },
        { "持仓盈亏率", "position_profit_pct_visible" },
    };

    // 期货模式下不支持的列（右键菜单灰显）
    public static readonly HashSet<string> FuturesUnsupported = new()
        { "买一", "卖一", "委比", "成交量", "成交额", "均价" };

    // 颜色（对应 Display.py UP_COLOR / DOWN_COLOR / NEUTRAL_COLOR）
    public const string UpColor = "#dd2100";       // 红涨
    public const string DownColor = "#019933";     // 绿跌
    public const string NeutralColor = "#494949";  // 中性灰

    // 列标题 -> 行情 sign 字段名（着色用）
    public static readonly Dictionary<string, string> HeaderSign = new()
    {
        { "现价", "delta" }, { "涨跌值", "delta" }, { "涨跌幅", "delta" },
        { "委比", "commi" }, { "均价", "avg" },
        { "买一", "b1" }, { "卖一", "s1" },
        { "持仓盈亏", "position_profit" }, { "持仓盈亏率", "position_profit" },
    };

    // 代码格式正则
    public static readonly Regex ReFutures = new(@"^nf_[a-zA-Z]{1,3}\d{3,4}$", RegexOptions.IgnoreCase);
    public static readonly Regex ReStock = new(@"^(sh|sz|bj)\d{6}$");
}
