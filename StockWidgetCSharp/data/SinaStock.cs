using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace StockWidget;

/// <summary>
/// 新浪股票行情抓取（对应 Python data.py）。
/// 返回与 SinaFutures 同构的 QuoteRow 列表。
/// </summary>
internal static class SinaStock
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // 注册 gbk 编码（新浪接口用 gbk）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var c = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None });
        c.DefaultRequestHeaders.Referrer = new Uri("https://finance.sina.com.cn");
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        c.Timeout = TimeSpan.FromSeconds(3);
        return c;
    }

    /// <summary>抓取股票行情。opts: shortCode, nameLength, b1s1Display。</summary>
    public static List<QuoteRow> Fetch(IEnumerable<string> codes, bool shortCode, int nameLength, string b1s1Display)
    {
        var label = string.Join(",", codes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
        if (string.IsNullOrEmpty(label)) throw new Exception("暂无数据，请添加自选");

        var url = "https://hq.sinajs.cn/list=" + label;
        var rows = new List<QuoteRow>();

        var bytes = Http.GetByteArrayAsync(url).GetAwaiter().GetResult();
        var gbk = Encoding.GetEncoding("gbk");
        var text = gbk.GetString(bytes);

        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrEmpty(line) || !line.Contains("\"")) continue;
            var eqIdx = line.IndexOf("=\"");
            if (eqIdx < 0) continue;
            var head = line[..eqIdx];
            var payload = line[(eqIdx + 2)..].TrimEnd('"', ';', '\r');
            var parts = payload.Split(',');
            if (parts.Length < 30) continue;

            var heads = head.Split('_');
            var code = heads.Length >= 3 ? heads[2] : "";

            ParseOneRow(parts, code, shortCode, nameLength, b1s1Display, rows);
        }
        return rows;
    }

    private static void ParseOneRow(string[] p, string code, bool shortCode, int nameLength, string b1s1Display, List<QuoteRow> rows)
    {
        var name = p[0];
        double opening = Num(p, 1);
        double prevClose = Num(p, 2);
        double current = Num(p, 3);
        double high = Num(p, 4);
        double low = Num(p, 5);
        double firstPur = Num(p, 6);
        double firstSell = Num(p, 7);
        double dealsVol = Num(p, 8);
        double dealsAmt = Num(p, 9);
        var purchaser = RangeInt(p, 10, 19, 2);  // 买盘量
        var seller = RangeInt(p, 20, 29, 2);     // 卖盘量

        bool etf = code.Length >= 3 && (code[2] == '1' || code[2] == '5');
        int dec = etf ? 3 : 2;

        bool AlmostEq(double a, double b) => Math.Round(a, dec) == Math.Round(b, dec);

        // 买一/卖一箭头标记
        var buyMarker = (firstPur > 0 && AlmostEq(current, firstPur)) ? "<" : " ";
        var sellMarker = (firstSell > 0 && AlmostEq(current, firstSell)) ? ">" : " ";

        string b1Label = "", s1Label = "";
        int b1Sign = 0, s1Sign = 0;

        string bPrice = firstPur.ToString($"F{dec}");
        string sPrice = firstSell.ToString($"F{dec}");

        if (firstPur == firstSell && firstSell > 0)
        {
            // 集合竞价
            current = firstSell;
            double paired = seller[0];
            double unpairedSign = seller[1] > 0 ? -seller[1] : purchaser[1];
            int pairedCnt = (int)(paired / 100);
            int unpairedCnt = (int)(unpairedSign / 100);
            string unpairedStr = (unpairedCnt >= 0 ? "+" : "") + unpairedCnt;
            if (b1s1Display == "price") { b1Label = bPrice; s1Label = sPrice; }
            else if (b1s1Display == "both") { b1Label = $"{pairedCnt}({bPrice})"; s1Label = $"{unpairedStr}({sPrice})"; }
            else { b1Label = $"{pairedCnt}"; s1Label = unpairedStr; }
            b1Sign = Math.Sign(unpairedSign);
            s1Sign = b1Sign;
        }
        else
        {
            // 连续竞价
            if (firstPur > 0)
            {
                var cnt = (int)(purchaser[0] / 100);
                if (b1s1Display == "price") b1Label = $"{bPrice}{buyMarker}";
                else if (b1s1Display == "both") b1Label = $"{cnt}({bPrice}){buyMarker}";
                else b1Label = $"{cnt}{buyMarker}";
            }
            else b1Label = $"-{buyMarker}";

            if (firstSell > 0)
            {
                var cnt = (int)(seller[0] / 100);
                if (b1s1Display == "price") s1Label = $"{sellMarker}{sPrice}";
                else if (b1s1Display == "both") s1Label = $"{sellMarker}{cnt}({sPrice})";
                else s1Label = $"{sellMarker}{cnt}";
            }
            else s1Label = $"{sellMarker}-";

            b1Sign = 1;   // 红涨
            s1Sign = -1;  // 绿跌
        }

        if (current == 0) current = prevClose;
        if (opening == 0) { opening = current; high = current; low = current; }
        if (high == 0) high = Math.Max(opening, Math.Max(current, prevClose));
        if (low == 0) low = Math.Min(opening, Math.Min(current, prevClose));

        double change = prevClose != 0 ? current - prevClose : 0;
        double changePct = prevClose != 0 ? (current / prevClose - 1) * 100 : 0;
        double avg = dealsVol > 0 ? dealsAmt / dealsVol : prevClose;
        double pSum = purchaser.Sum(), sSum = seller.Sum();
        double committee = (pSum + sSum) > 0 ? 100 * (pSum - sSum) / (pSum + sSum) : 0;

        string arrow = " ";
        if (high > low)
        {
            if (current == high) arrow = "↑";
            else if (current == low) arrow = "↓";
        }

        var row = new QuoteRow();
        row.Code = code;
        row.Cells[0] = shortCode && code.Length > 2 ? code[2..] : code;
        row.Cells[1] = nameLength == 0 ? name : (name.Length > nameLength ? name[..nameLength] : name);
        row.Cells[2] = $"{current.ToString($"F{dec}")}{arrow}";
        row.Cells[3] = (change >= 0 ? "+" : "") + change.ToString($"F{dec}");
        row.Cells[4] = $"{(changePct >= 0 ? "+" : "")}{changePct:F2}%";
        row.Cells[5] = b1Label;
        row.Cells[6] = s1Label;
        row.Cells[7] = $"{(committee >= 0 ? "+" : "")}{committee:F2}%";
        row.Cells[8] = FmtVol(dealsVol);
        row.Cells[9] = FmtAmount(dealsAmt);
        row.Cells[10] = avg.ToString($"F{dec}");
        row.Cells[11] = "";  // K 线列文本为空（UserRole 数据另存）
        row.KLine = (opening, current, high, low, prevClose);

        row.Delta = Math.Sign(change);
        row.Commi = Math.Sign(committee);
        row.Avg = avg > prevClose ? 1 : (avg < prevClose ? -1 : 0);
        row.B1 = b1Sign;
        row.S1 = s1Sign;
        rows.Add(row);
    }

    // ===== 辅助 =====

    private static double Num(string[] p, int i) => i < p.Length && double.TryParse(p[i], out var v) ? v : 0;

    private static int[] RangeInt(string[] p, int from, int toExclusive, int step)
    {
        var list = new List<int>();
        for (int i = from; i < toExclusive && i < p.Length; i += step)
            list.Add(int.TryParse(p[i], out var v) ? v : 0);
        return list.ToArray();
    }

    public static string FmtVol(double v)
    {
        if (v < 1e4) return v.ToString("0");
        if (v < 1e8) return (v / 1e4).ToString("F2") + "万";
        return (v / 1e8).ToString("F2") + "亿";
    }

    public static string FmtAmount(double v)
    {
        if (v < 1e8) return (v / 1e4).ToString("F2") + "万";
        if (v < 1e12) return (v / 1e8).ToString("F2") + "亿";
        return (v / 1e12).ToString("F2") + "万亿";
    }
}
