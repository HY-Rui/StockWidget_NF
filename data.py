# filename: data.py
# 行情数据抓取与解析（新浪财经接口）。从 WidgetPanel._get_price 抽离，保持返回结构不变。
import requests


def _get_opt(opts, key, default):
    """从 dict 或 namespace 取只读渲染参数，兼容两种传入方式。"""
    if isinstance(opts, dict):
        v = opts.get(key, default)
    else:
        v = getattr(opts, key, default)
    return v if v is not None else default


def _fmt_vol(v):
    """成交量格式化：<1万 显示原值，<1亿 显示万，否则显示亿。"""
    if v < 1e4:
        return f"{v}"
    if v < 1e8:
        return f"{v/1e4:.2f}万"
    return f"{v/1e8:.2f}亿"


def _fmt_amount(v):
    """成交额格式化：<1亿 显示万，<1万亿 显示亿，否则显示万亿。"""
    if v < 1e8:
        return f"{v/1e4:.2f}万"
    if v < 1e12:
        return f"{v/1e8:.2f}亿"
    return f"{v/1e12:.2f}万亿"


def fetch_quotes(codes, opts):
    """从新浪财经接口拉取行情并解析为表格行数据。

    参数：
        codes: list[str]  股票代码（带 sh/sz/bj 前缀）
        opts:  namespace/dict，含只读渲染参数：
            - short_code     bool   是否仅显示数字代码
            - name_length    int    名称截断长度（0=完整）
            - b1s1_display   str    'qty'|'price'|'both'

    返回：
        (price_data, sign_data)
        - price_data: list[list]  每行 12 列，与 ALL_HEADERS 对齐
        - sign_data:  list[dict]  每行颜色 sign（delta/commi/avg/b1/s1）
    """
    short_code   = bool(_get_opt(opts, 'short_code', False))
    name_length  = int(_get_opt(opts, 'name_length', 0))
    b1s1_display = _get_opt(opts, 'b1s1_display', 'qty')

    label = ",".join([str(c).strip() for c in codes if str(c).strip()])
    if not label:
        raise Exception("暂无数据，请添加自选")

    price_data = []
    sign_data = []
    url = 'https://hq.sinajs.cn/list=' + label
    headers = {'Referer': 'https://finance.sina.com.cn', 'User-Agent': 'Mozilla/5.0'}
    r = requests.get(url, headers=headers, timeout=3)
    r.encoding = 'gbk'
    for line in r.text.split('\n'):
        if not line or '"' not in line:
            continue
        heads = line.split('="')[0].split('_')
        parts = line.split('="')[1].split(',')
        if len(parts) < 30:
            continue

        code          = heads[2]
        name          = parts[0]
        opening_price = float(parts[1] or 0)   # 开盘
        prev_close    = float(parts[2] or 0)   # 昨收
        current_price = float(parts[3] or 0)   # 现价
        high_price    = float(parts[4] or 0)   # 当日最高
        low_price     = float(parts[5] or 0)   # 当日最低
        first_pur     = float(parts[6] or 0)   # 买一
        first_sell    = float(parts[7] or 0)   # 卖一
        deals_vol     = float(parts[8] or 0)   # 成交量
        deals_amt     = float(parts[9] or 0)   # 成交额
        purchaser     = [int(x or 0) for x in parts[10:19:2]]  # 买盘，股数
        seller        = [int(x or 0) for x in parts[20:29:2]]  # 卖盘，股数

        etf = code[2] in ('1', '5')

        # 小数精度：ETF 3 位，股票 2 位（用于显示与近似相等比较）
        dec = 3 if etf else 2
        price_fmt = f"{{:.{dec}f}}"

        def almost_eq(a, b):
            try:
                return round(float(a), dec) == round(float(b), dec)
            except Exception:
                return False

        # 构建买一/卖一数据及其颜色信息，并添加位置箭头
        b1_label = ""
        s1_label = ""
        b1_color_sign = 0  # 买一颜色：1红 0中性 -1绿
        s1_color_sign = 0  # 卖一颜色：1红 0中性 -1绿

        # 标记：买一箭头位于右侧 '<'，卖一箭头位于左侧 '>'
        buy_marker = "<" if (first_pur > 0 and almost_eq(current_price, first_pur)) else " "
        sell_marker = ">" if (first_sell > 0 and almost_eq(current_price, first_sell)) else " "

        b_price = price_fmt.format(first_pur)
        s_price = price_fmt.format(first_sell)

        if first_pur == first_sell > 0:
            # 集合竞价：配对量 / 未配对量
            current_price = first_sell  # 9:15 ~ 9:25; 14:57 ~ 15:00 竞价
            paired = seller[0]
            # unpaired_sign: >0 表示买方优势，<0 表示卖方优势
            unpaired_sign = -seller[1] if seller[1] > 0 else purchaser[1]
            paired_cnt = int(paired/100)
            unpaired_cnt = int(unpaired_sign/100)
            mode = b1s1_display
            if mode == 'price':
                b1_label, s1_label = b_price, s_price
            elif mode == 'both':
                b1_label = f"{paired_cnt:d}({b_price})"
                s1_label = f"{unpaired_cnt:+d}({s_price})"
            else:
                b1_label, s1_label = f"{paired_cnt:d}", f"{unpaired_cnt:+d}"
            # 竞价颜色：根据未配对量的方向
            b1_color_sign = (unpaired_sign > 0) - (unpaired_sign < 0)
            s1_color_sign = b1_color_sign
        else:
            # 连续竞价：买一数量/卖一数量
            if first_pur > 0:
                cnt = f"{int(purchaser[0]/100)}"
                mode = b1s1_display
                if mode == 'price':
                    b1_label = f"{b_price}{buy_marker}"
                elif mode == 'both':
                    b1_label = f"{cnt}({b_price}){buy_marker}"
                else:
                    b1_label = f"{cnt}{buy_marker}"
            else:
                b1_label = f"-{buy_marker}"

            if first_sell > 0:
                cnt = f"{int(seller[0]/100)}"
                mode = b1s1_display
                if mode == 'price':
                    s1_label = f"{sell_marker}{s_price}"
                elif mode == 'both':
                    s1_label = f"{sell_marker}{cnt}({s_price})"
                else:
                    s1_label = f"{sell_marker}{cnt}"
            else:
                s1_label = f"{sell_marker}-"

            # 连续竞价时：买一固定红色，卖一固定绿色
            b1_color_sign = 1
            s1_color_sign = -1

        if current_price == 0:
            current_price = prev_close # 9:00 ~ 9:15 无数据
        if opening_price == 0:
            opening_price = current_price
            high_price = current_price
            low_price = current_price

        change = current_price - prev_close if prev_close else 0.0
        change_pct = (current_price / prev_close - 1) * 100 if prev_close else 0.0
        avg = (deals_amt / deals_vol) if deals_vol > 0 else prev_close # 均价
        p_sum, s_sum = sum(purchaser), sum(seller)
        committee = (100 * (p_sum - s_sum) / (p_sum + s_sum)) if (p_sum + s_sum) > 0 else 0.0 # 委比

        # 触及日高/低显示箭头
        arrow = " "
        if high_price > low_price:
            if current_price == high_price: arrow = "↑"
            elif current_price == low_price: arrow = "↓"

        k_payload = {"k": (opening_price, current_price, high_price, low_price, prev_close)}

        # "代码", "名称", "现价", "涨跌值", "涨跌幅", "买一", "卖一", "委比", "成交量", "成交额", "均价",  "K线"
        disp_code = code[2:] if short_code else code
        disp_name = name if name_length == 0 else name[:name_length]
        price_data.append([
            disp_code,
            disp_name,
            f"{price_fmt.format(current_price)}{arrow}",
            f"{change:+.{dec}f}",
            f"{change_pct:+.2f}%",
            b1_label,
            s1_label,
            f"{committee:+.2f}%",
            _fmt_vol(deals_vol),
            _fmt_amount(deals_amt),
            price_fmt.format(avg),
            k_payload
        ])
        sign_data.append({
            "delta": (change > 0) - (change < 0),
            "commi": (committee > 0) - (committee < 0),
            "avg": (avg > prev_close) - (avg < prev_close),
            "b1": b1_color_sign,
            "s1": s1_color_sign,
        })

    return price_data, sign_data

