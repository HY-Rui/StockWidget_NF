namespace StockWidget;

/// <summary>
/// 单条行情行：12 个显示单元格 + 颜色 sign。
/// 对应 Python 版 price_data 一行 + sign_data 一项。
/// K 线五元组存于 KLine 字段（(开, 现, 高, 低, 昨收)）。
/// </summary>
internal sealed class QuoteRow
{
    public string Code { get; set; } = "";

    // 显示文本，索引对齐 Constants.AllHeaders
    public string[] Cells { get; } = new string[Constants.AllHeaders.Length];

    // 颜色 sign：-1 绿 / 0 中性 / 1 红
    public int Delta { get; set; }   // 现价/涨跌值/涨跌幅
    public int Commi { get; set; }   // 委比
    public int Avg { get; set; }     // 均价
    public int B1 { get; set; }      // 买一
    public int S1 { get; set; }      // 卖一
    public int PositionProfit { get; set; } // 持仓盈亏

    // K 线数据：(开盘, 现价, 最高, 最低, 昨收)，仅 K 线列用
    public (double O, double C, double H, double L, double P)? KLine { get; set; }

    /// <summary>按列标题取该列的 sign（用于着色）。未知列返回 0。</summary>
    public int SignFor(string header) => header switch
    {
        "现价" or "涨跌值" or "涨跌幅" => Delta,
        "委比" => Commi,
        "均价" => Avg,
        "买一" => B1,
        "卖一" => S1,
        "持仓盈亏" or "持仓盈亏率" => PositionProfit,
        _ => 0,
    };
}

/// <summary>
/// UI 专用行情行视图：把解析结果投影到当前可见列。
/// </summary>
internal sealed class QuoteRowView
{
    private readonly QuoteRow _source;

    public QuoteRowView(QuoteRow source, string[] headers)
    {
        _source = source;
        Headers = headers;
    }

    public string[] Headers { get; }

    public string CellText(int visibleIndex)
    {
        var globalIndex = Array.IndexOf(Constants.AllHeaders, Headers[visibleIndex]);
        return globalIndex >= 0 ? _source.Cells[globalIndex] : "";
    }

    public (double O, double C, double H, double L, double P)? KLine => _source.KLine;

    public int SignFor(int visibleIndex) => _source.SignFor(Headers[visibleIndex]);
}
