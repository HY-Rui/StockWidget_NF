namespace StockWidget;

internal sealed class PositionInfo
{
    public double CostPrice { get; set; }
    public double Quantity { get; set; }
    public string Direction { get; set; } = "long";

    public bool IsShort => string.Equals(Direction, "short", StringComparison.OrdinalIgnoreCase);
    public string DirectionText => IsShort ? "做空" : "做多";

    public PositionInfo Clone() => new()
    {
        CostPrice = CostPrice,
        Quantity = Quantity,
        Direction = IsShort ? "short" : "long"
    };

    public bool IsValid => CostPrice > 0 && Quantity > 0;

    public double ProfitAt(double price)
    {
        var priceChange = IsShort ? CostPrice - price : price - CostPrice;
        return priceChange * Quantity;
    }
}
