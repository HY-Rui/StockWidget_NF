# filename: futures_data.py
# 国内期货行情抓取与解析（新浪财经接口）。
#
# 重要：新浪期货接口统一使用 nf_ 前缀（hf_ 是外盘期货，不是国内商品期货）。
# 国内各交易所（上期/大商/广期/郑商/中金）的合约在 nf_ 下均可取到，且非交易时段返回上一交易时段缓存，
# 因此与股票一样在盘外也能看到最近价格。
#
# 字段布局有两种（按返回字段数自动分流）：
#   - 商品期货（44字段）：[0]名称 [2]昨结 [3]开盘 [4]最高 [6]最低 [8]现价
#                          [13]成交量(手) [14]持仓量 [15]交易所简称 [16]品种名 [17]日期
#   - 中金所股指/国债（50字段）：[0]昨结 [1]开盘 [2]最高 [3]最低 [7]现价 [49]名称
#
# 返回结构与 data.fetch_quotes 完全一致：期货不支持的列(盘口/委比/成交量/成交额/均价)留空字符串。
import requests
from data import _get_opt


def _fnum(parts, idx):
    """安全取期货字段为 float，空/无效返回 0.0。"""
    try:
        v = parts[idx]
        return float(v) if (v is not None and str(v).strip() != "") else 0.0
    except (IndexError, ValueError, TypeError):
        return 0.0


def _fmt_price(v):
    """期货价格原样格式化（商品价位不一，保留有意义的小数位，去尾零）。"""
    if v == 0:
        return "0"
    try:
        s = f"{float(v):.4f}".rstrip("0").rstrip(".")
        return s if s else "0"
    except Exception:
        return str(v)


def _abbrev_name(code, name):
    """品种缩写 + 月份：从代码提取品种缩写（nf_RB2610→RB），拼上名称末尾的月份数字。
    例：('nf_RB2610','螺纹钢2610')→'RB2610'；('nf_IF2606','沪深300指数期货2606')→'IF2606'。
    无法提取时回退到原名称。"""
    import re as _re
    # 代码：nf_ + 品种字母 + 月份数字。提取品种字母部分
    m = _re.match(r'^[a-zA-Z]{1,3}_[a-zA-Z]{1,3}(?=\d)', code)
    sym = m.group(0).split('_', 1)[1] if m else None
    # 名称末尾的月份数字（3-4位）
    mm = _re.search(r'(\d{3,4})\s*$', name)
    month = mm.group(1) if mm else ""
    if sym:
        return f"{sym}{month}" if month else sym
    return name


# 字段索引布局：商品期货 vs 中金股指国债，名称位置和关键字段索引不同
# (name_idx, prev_idx, open_idx, high_idx, low_idx, cur_idx)
_FIELD_LAYOUT = {
    "commodity": (0, 2, 3, 4, 6, 8),    # 商品期货 44 字段
    "index":     (-1, 0, 1, 2, 3, 7),   # 中金股指 50 字段（名称在末尾 parts[-1]）
}


def _parse(parts, code, layout):
    """按字段布局解析期货行情，返回 (name, current, change, change_pct, arrow, k_payload)。"""
    name_idx, prev_idx, open_idx, high_idx, low_idx, cur_idx = layout
    # 名称：商品在 [0]，股指在末尾 [49]
    if name_idx >= 0:
        name = parts[name_idx].strip() or code
    else:
        name = parts[-1].strip() if (len(parts) > 30 and parts[-1].strip()) else code

    prev_settle = _fnum(parts, prev_idx)
    opening     = _fnum(parts, open_idx)
    high        = _fnum(parts, high_idx)
    low         = _fnum(parts, low_idx)
    current     = _fnum(parts, cur_idx)

    if current == 0:
        current = prev_settle  # 极端兜底
    change = (current - prev_settle) if prev_settle else 0.0
    change_pct = ((current / prev_settle - 1) * 100) if prev_settle else 0.0

    arrow = " "
    if high > low:
        if current == high:
            arrow = "↑"
        elif current == low:
            arrow = "↓"
    k_payload = {"k": (opening, current, high, low, prev_settle)}
    return name, current, change, change_pct, arrow, k_payload


def fetch_futures(codes, opts):
    """从新浪期货接口拉取国内期货行情。

    参数：
        codes: list[str]  完整期货代码，统一 nf_ 前缀
               （如 'nf_RB2610' 上期螺纹、'nf_M2609' 大商豆粕、'nf_IF2606' 中金股指、
                 'nf_TA2609' 郑商PTA、'nf_SI2612' 广期工业硅）
        opts:  dict，含只读渲染参数：
               - short_code  bool  是否仅显示合约字母+数字部分（去掉 nf_ 前缀）
               - name_length int   名称截断长度（0=完整）
               - abbrev     bool  名称用品种缩写+月份（如 螺纹钢2610→RB2610）

    返回：
        (price_data, sign_data)，与 data.fetch_quotes 同构（12列 + sign字典）。
        期货不支持的列位置留 ""。
    """
    short_code  = bool(_get_opt(opts, 'short_code', False))
    name_length = int(_get_opt(opts, 'name_length', 0))
    abbrev      = bool(_get_opt(opts, 'abbrev', False))

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
        try:
            head, payload = line.split('="', 1)
            payload = payload.rstrip('";').rstrip('"')
        except ValueError:
            continue
        parts = payload.split(',')
        if len(parts) < 10:
            continue
        if not any(p and p.strip() for p in parts):
            continue

        # 代码：从 var 名提取（var hq_str_nf_RB2610=...）
        try:
            code = head.split('_', 2)[-1] if head.count('_') >= 2 else head.split('_')[-1]
        except Exception:
            code = ""

        # 按字段数分流：商品期货 44 字段 / 中金股指国债 50 字段
        layout = _FIELD_LAYOUT["index"] if len(parts) >= 48 else _FIELD_LAYOUT["commodity"]
        name, current, change, change_pct, arrow, k_payload = _parse(parts, code, layout)

        # 显示代码：short_code 时去掉 nf_ 前缀
        if short_code and code.lower().startswith("nf_"):
            disp_code = code[3:]
        else:
            disp_code = code

        # 名称：缩写模式优先（品种缩写 + 名称末尾的月份数字）
        if abbrev:
            disp_name = _abbrev_name(code, name)
        elif name_length == 0:
            disp_name = name
        else:
            disp_name = name[:name_length]

        # "代码", "名称", "现价", "涨跌值", "涨跌幅", "买一", "卖一", "委比", "成交量", "成交额", "均价",  "K线"
        price_data.append([
            disp_code,
            disp_name,
            f"{_fmt_price(current)}{arrow}",
            f"{change:+.2f}",
            f"{change_pct:+.2f}%",
            "",
            "",
            "",
            "",
            "",
            "",
            k_payload,
        ])
        sign_data.append({
            "delta": (change > 0) - (change < 0),
            "commi": 0,
            "avg": 0,
            "b1": 0,
            "s1": 0,
        })

    return price_data, sign_data
