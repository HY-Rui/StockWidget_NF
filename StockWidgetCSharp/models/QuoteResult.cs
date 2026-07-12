namespace StockWidget;

internal enum QuoteStatus
{
    Ok,
    NoCheckedCodes,
    NetworkError,
    EmptyResponse,
    ParseError
}

internal sealed class QuoteResult
{
    public QuoteStatus Status { get; init; }
    public List<QuoteRow> Rows { get; init; } = new();
    public string Message { get; init; } = "";
    public bool KeepPreviousRows { get; init; }

    public static QuoteResult Ok(List<QuoteRow> rows) => new()
    {
        Status = QuoteStatus.Ok,
        Rows = rows
    };

    public static QuoteResult Error(QuoteStatus status, string message, bool keepPreviousRows = true) => new()
    {
        Status = status,
        Message = message,
        KeepPreviousRows = keepPreviousRows
    };
}
