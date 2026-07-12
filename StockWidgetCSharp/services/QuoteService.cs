using System.Net.Http;
using System.Threading;
using System.Windows.Threading;

namespace StockWidget;

/// <summary>
/// 行情刷新调度（对应 Python WidgetPanel 的 timer + _refresh_from_function）。
/// 按配置的刷新间隔定时抓取行情，隐藏时暂停。
/// </summary>
internal sealed class QuoteService : IDisposable
{
    private const double MinRefreshSeconds = 0.1;
    private readonly DispatcherTimer _timer;
    private readonly AppConfig _cfg;
    private List<string> _codes = new();
    private bool _running;
    private int _refreshing;

    /// <summary>行情刷新完成。异常状态会要求浮窗保留上一帧可用行情。</summary>
    public event Action<QuoteResult>? Refreshed;

    public QuoteService(AppConfig cfg)
    {
        _cfg = cfg;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(Math.Max(MinRefreshSeconds, cfg.RefreshSeconds))
        };
        _timer.Tick += OnTick;
    }

    /// <summary>当前要查询的代码列表（显示代码与持仓代码的并集）。</summary>
    public void SetCodes(List<string> codes) => _codes = codes.ToList();

    /// <summary>更新刷新间隔。</summary>
    public void SetInterval(double seconds)
    {
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(MinRefreshSeconds, seconds));
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _timer.Start();
        // 立即刷新一次（后台线程，避免阻塞 UI）
        RefreshNow();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _timer.Stop();
    }

    public void RefreshNow() => Task.Run(DoRefresh);

    private void OnTick(object? s, EventArgs e) => RefreshNow();

    private void DoRefresh()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        try
        {
            var codes = _codes.ToList();
            if (codes.Count == 0)
            {
                Refreshed?.Invoke(QuoteResult.Error(QuoteStatus.NoCheckedCodes, "未选择显示项", keepPreviousRows: false));
                return;
            }

            List<QuoteRow>? rows;
            if (_cfg.Market == "futures")
                rows = SinaFutures.Fetch(codes, _cfg.ShortCode, _cfg.NameLength, _cfg.FutAbbrev);
            else
                rows = SinaStock.Fetch(codes, _cfg.ShortCode, _cfg.NameLength, _cfg.B1s1Display);
            Refreshed?.Invoke(rows.Count > 0
                ? QuoteResult.Ok(rows)
                : QuoteResult.Error(QuoteStatus.EmptyResponse, "接口返回空数据"));
        }
        catch (Exception ex)
        {
            // 网络异常转友好提示
            bool networkError = ex is HttpRequestException || ex is TaskCanceledException;
            string msg = networkError ? "无网络连接" : $"行情解析失败：{ex.Message}";
            Refreshed?.Invoke(QuoteResult.Error(networkError ? QuoteStatus.NetworkError : QuoteStatus.ParseError, msg));
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= OnTick;
    }
}
