namespace StockWidget;

internal sealed class MarketDisplayConfig
{
    public bool PositionSummaryVisible { get; set; } = true;
    public bool ShortCode { get; set; }
    public int NameLength { get; set; }
    public bool FutAbbrev { get; set; }
    public string B1s1Display { get; set; } = "qty";

    public bool CodeVisible { get; set; }
    public bool NameVisible { get; set; }
    public bool PriceVisible { get; set; }
    public bool ChangeVisible { get; set; }
    public bool ChangePctVisible { get; set; }
    public bool B1s1Visible { get; set; }
    public bool CommiVisible { get; set; }
    public bool VolVisible { get; set; }
    public bool AmountVisible { get; set; }
    public bool AvgVisible { get; set; }
    public bool KlineVisible { get; set; }
    public bool PositionCostVisible { get; set; }
    public bool PositionQuantityVisible { get; set; }
    public bool PositionProfitVisible { get; set; }
    public bool PositionProfitPctVisible { get; set; }

    public double RefreshSeconds { get; set; } = 0.2;

    public MarketDisplayConfig Clone() => (MarketDisplayConfig)MemberwiseClone();
}
