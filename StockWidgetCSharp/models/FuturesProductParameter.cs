namespace StockWidget;

internal sealed class FuturesProductParameter
{
    public double TickSize { get; set; } = 1;
    public double TickProfit { get; set; }

    public bool IsValid => TickSize > 0 && TickProfit > 0;

    public FuturesProductParameter Clone() => new()
    {
        TickSize = TickSize,
        TickProfit = TickProfit
    };
}

internal static class FuturesProductCode
{
    public static string ForDisplay(string? contract)
    {
        if (string.IsNullOrWhiteSpace(contract)) return "";
        var value = contract.Trim();
        return value.StartsWith("nf_", StringComparison.OrdinalIgnoreCase) ? value[3..] : value;
    }

    public static string? FromContract(string? contract)
    {
        if (string.IsNullOrWhiteSpace(contract)) return null;
        var value = ForDisplay(contract);

        var length = 0;
        while (length < value.Length && char.IsLetter(value[length])) length++;
        return length > 0 ? value[..length].ToUpperInvariant() : null;
    }
}

internal sealed record FuturesProductSpec(string Name, string Code, double TickSize, double TickProfit);

internal static class FuturesProductCatalog
{
    private static readonly FuturesProductSpec[] Items =
    {
        new("铸造铝", "AD", 5, 50),
        new("沪银", "AG", 1, 15),
        new("沪铝", "AL", 5, 25),
        new("氧化铝", "AO", 1, 20),
        new("沪金", "AU", 0.02, 20),
        new("丁二烯胶", "BR", 5, 25),
        new("沥青", "BU", 1, 10),
        new("沪铜", "CU", 10, 50),
        new("燃油", "FU", 1, 10),
        new("热卷", "HC", 1, 10),
        new("沪镍", "NI", 10, 10),
        new("胶版纸", "OP", 2, 80),
        new("沪铅", "PB", 5, 25),
        new("螺纹钢", "RB", 1, 10),
        new("橡胶", "RU", 5, 50),
        new("沪锡", "SN", 10, 10),
        new("纸浆", "SP", 2, 20),
        new("不锈钢", "SS", 5, 25),
        new("线材", "WR", 1, 10),
        new("沪锌", "ZN", 5, 25),
        new("国际铜", "BC", 10, 50),
        new("欧线集运", "EC", 0.1, 5),
        new("低硫燃油", "LU", 1, 10),
        new("20号胶", "NR", 5, 50),
        new("原油", "SC", 0.1, 100),
        new("碳酸锂", "LC", 20, 20),
        new("钯", "PD", 0.05, 50),
        new("多晶硅", "PS", 5, 15),
        new("铂", "PT", 0.05, 50),
        new("工业硅", "SI", 5, 25),
        new("苹果", "AP", 1, 10),
        new("棉花", "CF", 5, 25),
        new("红枣", "CJ", 5, 25),
        new("棉纱", "CY", 5, 25),
        new("玻璃", "FG", 1, 20),
        new("粳稻", "JR", 1, 20),
        new("甲醇", "MA", 1, 10),
        new("菜油", "OI", 1, 10),
        new("短纤", "PF", 2, 10),
        new("花生", "PK", 2, 10),
        new("丙烯", "PL", 1, 20),
        new("普麦", "PM", 1, 50),
        new("瓶片", "PR", 2, 30),
        new("对二甲苯", "PX", 2, 10),
        new("早籼稻", "RI", 1, 20),
        new("菜粕", "RM", 1, 10),
        new("菜籽", "RS", 1, 10),
        new("纯碱", "SA", 1, 20),
        new("硅铁", "SF", 2, 10),
        new("烧碱", "SH", 1, 30),
        new("锰硅", "SM", 2, 10),
        new("白糖", "SR", 1, 10),
        new("PTA", "TA", 2, 10),
        new("尿素", "UR", 1, 20),
        new("强麦", "WH", 1, 20),
        new("动力煤", "ZC", 0.2, 20),
        new("豆一", "A", 1, 10),
        new("豆二", "B", 1, 10),
        new("胶合板", "BB", 0.05, 25),
        new("纯苯", "BZ", 1, 30),
        new("玉米", "C", 1, 10),
        new("淀粉", "CS", 1, 10),
        new("苯乙烯", "EB", 1, 5),
        new("乙二醇", "EG", 1, 10),
        new("纤维板", "FB", 0.5, 5),
        new("铁矿石", "I", 0.5, 50),
        new("焦炭", "J", 0.5, 50),
        new("鸡蛋", "JD", 1, 10),
        new("焦煤", "JM", 0.5, 30),
        new("塑料", "L", 1, 5),
        new("原木", "LG", 0.5, 45),
        new("生猪", "LH", 5, 80),
        new("豆粕", "M", 1, 10),
        new("棕榈油", "P", 2, 20),
        new("LPG", "PG", 1, 20),
        new("聚丙烯", "PP", 1, 5),
        new("粳米", "RR", 1, 10),
        new("PVC", "V", 1, 5),
        new("豆油", "Y", 2, 20),
        new("中证500股指", "IC", 0.2, 40),
        new("沪深", "IF", 0.2, 60),
        new("上证", "IH", 0.2, 60),
        new("中证1000股指", "IM", 0.2, 40),
        new("十债", "T", 0.005, 50),
        new("五债", "TF", 0.005, 50),
        new("三十债", "TL", 0.01, 100),
        new("二债", "TS", 0.002, 40),
    };

    private static readonly Dictionary<string, FuturesProductSpec> ByCode =
        Items.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<FuturesProductSpec> All => Items;

    public static bool TryGet(string? product, out FuturesProductSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(product) && ByCode.TryGetValue(product, out var found))
        {
            spec = found;
            return true;
        }
        spec = null!;
        return false;
    }
}
