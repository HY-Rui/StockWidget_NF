using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace StockWidget;

/// <summary>
/// 新浪国内期货行情抓取（对应 Python futures_data.py）。
/// 统一 nf_ 前缀；商品期货 44 字段、中金股指 50 字段，按返回字段数自动分流。
/// 期货不支持的列（盘口/委比/成交量/成交额/均价）留空。
/// </summary>
internal static class SinaFutures
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var c = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None });
        c.DefaultRequestHeaders.Referrer = new Uri("https://finance.sina.com.cn");
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        c.Timeout = TimeSpan.FromSeconds(3);
        return c;
    }

    /// <summary>抓取期货行情。nf_ 仅用于请求，显示代码固定省略该前缀。</summary>
    public static List<QuoteRow> Fetch(IEnumerable<string> codes, int nameLength, bool abbrev)
    {
        var label = string.Join(",", codes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
        if (string.IsNullOrEmpty(label)) throw new Exception("暂无数据，请添加自选");

        var url = "https://hq.sinajs.cn/list=" + label;
        var bytes = Http.GetByteArrayAsync(url).GetAwaiter().GetResult();
        var gbk = Encoding.GetEncoding("gbk");
        var text = gbk.GetString(bytes);

        var rows = new List<QuoteRow>();
        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrEmpty(line) || !line.Contains("\"")) continue;
            var eqIdx = line.IndexOf("=\"");
            if (eqIdx < 0) continue;
            var head = line[..eqIdx];
            var payload = line[(eqIdx + 2)..].TrimEnd('"', ';', '\r');
            var parts = payload.Split(',');
            if (parts.Length < 10) continue;
            if (!parts.Any(x => !string.IsNullOrWhiteSpace(x))) continue;

            // 代码：var hq_str_nf_RB2610=... -> 取 nf_RB2610
            var code = ExtractCode(head);

            // 按字段数分流：股指 >=48，商品 <48
            bool isIndex = parts.Length >= 48;
            string name;
            double prev, opening, high, low, current;
            if (isIndex)
            {
                name = parts[^1].Trim(); if (string.IsNullOrEmpty(name)) name = code;
                prev = Num(parts, 0);
                opening = Num(parts, 1);
                high = Num(parts, 2);
                low = Num(parts, 3);
                current = Num(parts, 7);
            }
            else
            {
                name = parts[0].Trim(); if (string.IsNullOrEmpty(name)) name = code;
                prev = Num(parts, 2);
                opening = Num(parts, 3);
                high = Num(parts, 4);
                low = Num(parts, 6);
                current = Num(parts, 8);
            }

            if (current == 0) current = prev;
            if (opening == 0) opening = current;
            if (high == 0) high = Math.Max(opening, Math.Max(current, prev));
            if (low == 0) low = Math.Min(opening, Math.Min(current, prev));
            double change = prev != 0 ? current - prev : 0;
            double changePct = prev != 0 ? (current / prev - 1) * 100 : 0;

            string arrow = " ";
            if (high > low)
            {
                if (current == high) arrow = "↑";
                else if (current == low) arrow = "↓";
            }

            // 显示名称：缩写优先
            string dispName;
            if (abbrev) dispName = AbbrevName(code, name);
            else if (nameLength == 0) dispName = name;
            else dispName = name.Length > nameLength ? name[..nameLength] : name;

            // 显示代码
            string dispCode = FuturesProductCode.ForDisplay(code);

            var row = new QuoteRow();
            row.Code = code;
            row.Cells[0] = dispCode;
            row.Cells[1] = dispName;
            row.Cells[2] = FmtPrice(current) + arrow;
            row.Cells[3] = (change >= 0 ? "+" : "") + change.ToString("F2");
            row.Cells[4] = $"{(changePct >= 0 ? "+" : "")}{changePct:F2}%";
            // 期货不支持的列留空
            row.Cells[5] = ""; row.Cells[6] = ""; row.Cells[7] = "";
            row.Cells[8] = ""; row.Cells[9] = ""; row.Cells[10] = "";
            row.Cells[11] = "";
            row.KLine = (opening, current, high, low, prev);
            row.Delta = Math.Sign(change);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>从 var 名提取代码：var hq_str_nf_RB2610 -> nf_RB2610。</summary>
    private static string ExtractCode(string head)
    {
        const string prefix = "var hq_str_";
        if (head.StartsWith(prefix, StringComparison.Ordinal))
            return head[prefix.Length..];
        var idx = head.IndexOf("hq_str_", StringComparison.Ordinal);
        return idx >= 0 ? head[(idx + "hq_str_".Length)..] : head;
    }

    /// <summary>期货价格格式化：保留有意义小数位，去尾零（对应 _fmt_price）。</summary>
    public static string FmtPrice(double v)
    {
        if (v == 0) return "0";
        var s = v.ToString("F4").TrimEnd('0').TrimEnd('.');
        return string.IsNullOrEmpty(s) ? "0" : s;
    }

    /// <summary>品种缩写+月份：螺纹钢2610→RB2610（对应 _abbrev_name）。</summary>
    public static string AbbrevName(string code, string name)
    {
        // 从代码提取品种字母（nf_RB2610 → RB）
        string? sym = null;
        var m = Regex.Match(code, @"^[a-zA-Z]{1,3}_([a-zA-Z]{1,3})(?=\d)");
        if (m.Success) sym = m.Groups[1].Value;

        // 从名称末尾提取月份数字
        string month = "";
        var mm = Regex.Match(name, @"(\d{3,4})\s*$");
        if (mm.Success) month = mm.Groups[1].Value;

        if (sym != null) return string.IsNullOrEmpty(month) ? sym : sym + month;
        return name;
    }

    private static double Num(string[] p, int i) => i < p.Length && double.TryParse(p[i], out var v) ? v : 0;
}
