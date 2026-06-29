import re
import keyboard
import requests
from functools import partial

from PySide6.QtCore import Qt, QEvent, QTimer, Signal
from PySide6.QtGui import QFont, QAction, QColor
from PySide6.QtWidgets import QApplication, QWidget, QMenu, QVBoxLayout, QLabel, QTableView, QHeaderView, QAbstractItemView, QFrame, QStyledItemDelegate

from Display import SimpleTableModel, KLineDelegate
from data import fetch_quotes
from futures_data import fetch_futures

# 列标题 -> 可见性属性名 的映射（数据驱动，消除多处重复 if-elif）
# 买一/卖一 共用 b1s1_visible
_HEADER_ATTRS = {
    "代码": "code_visible",
    "名称": "name_visible",
    "现价": "price_visible",
    "涨跌值": "change_visible",
    "涨跌幅": "change_pct_visible",
    "买一": "b1s1_visible",
    "卖一": "b1s1_visible",
    "委比": "commi_visible",
    "成交量": "vol_visible",
    "成交额": "amount_visible",
    "均价": "avg_visible",
    "K线": "kline_visible",
}
# 期货模式下不支持的列（无对应数据，菜单中灰显）
_FUTURES_UNSUPPORTED = ("买一", "卖一", "委比", "成交量", "成交额", "均价")

# 期货代码格式（新浪接口要求 nf_ 前缀 + 大写合约字母）
_RE_FUTURES = re.compile(r'^nf_[a-zA-Z]{1,3}\d{3,4}$', re.IGNORECASE)
_RE_STOCK = re.compile(r'^(sh|sz|bj)\d{6}$')


class FloatLabel(QWidget):
    hotkey_triggered = Signal()
    def __init__(self, cfg: dict):
        super().__init__()
        self._on_change = (lambda: None)
        self._open_settings_cb = None
        self._quit_cb = None

        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.Tool)
        self.setAttribute(Qt.WA_TranslucentBackground, True)
        self.setFocusPolicy(Qt.StrongFocus)

        # 加载配置
        self.market             = cfg.get("market", "stock")                # 市场：stock(股票) / futures(国内期货)
        if self.market not in ("stock", "futures"):
            self.market = "stock"
        codes_cfg               = cfg.get("codes",["sh000001"])             # 自选列表
        checked_codes_cfg       = cfg.get("checked_codes", cfg.get("visible_codes", codes_cfg))  # 在浮窗中显示的股票（新名 checked_codes，兼容 visible_codes）
        self.refresh_seconds    = float(cfg.get("refresh_seconds", 2))        # 刷新间隔(秒，支持亚秒如0.5)
        flags_cfg               = cfg.get("flags", {})                      # 指标开关（字典格式）
        self.short_code         = bool(cfg.get("short_code", False))
        self.name_length        = int(cfg.get("name_length",0))
        self.fut_abbrev         = bool(cfg.get("fut_abbrev", False))   # 期货名称用品种缩写
        # b1s1_display: 'qty'|'price'|'both'。兼容旧配置键 b1s1_price (bool)
        b1s1_display_cfg = cfg.get("b1s1_display", None)
        if isinstance(b1s1_display_cfg, str) and b1s1_display_cfg in ("qty", "price", "both"):
            self.b1s1_display = b1s1_display_cfg
        else:
            # 旧配置兼容：若 b1s1_price 为 True 则默认显示价格，否则显示数量
            self.b1s1_display = "price" if bool(cfg.get("b1s1_price", False)) else "qty"
        
        # 防止买一/卖一同步时触发重复处理
        self._syncing_b1s1 = False

        self.header_visible     = bool(cfg.get("header_visible", False))    # 表头可见
        self.grid_visible       = bool(cfg.get("grid_visible", False))      # 网格可见

        font_family             = cfg.get("font_family", "Microsoft YaHei") # 字体类型
        font_size               = int(cfg.get("font_size", 10))             # 字体大小
        self.line_extra_px      = int(cfg.get("line_extra_px", 1))          # 行间距
        self.fg                 = QColor(cfg.get("fg", "#FFFFFF"))        # 前景色
        bg                      = cfg.get("bg", {"r":0,"g":0,"b":0,"a":191})# 背景色
        self.opacity_pct        = int(cfg.get("opacity_pct", 90))           # 透明度
        self.default_color      = bool(cfg.get("default_color", False))     # 默认颜色模式

        self.hotkey             = cfg.get("hotkey", "Ctrl+Alt+F")           # 快捷键
        self.start_on_boot      = bool(cfg.get("start_on_boot", False))

        # 列标题列表（与 _HEADER_ATTRS 键顺序保持一致）
        self.ALL_HEADERS = list(_HEADER_ATTRS.keys())

        # 列显示标志（独立属性）
        # 解析旧 flags 配置以做回退
        old_flags = {}
        if isinstance(flags_cfg, list):
            for i, h in enumerate(self.ALL_HEADERS):
                old_flags[h] = bool(flags_cfg[i]) if i < len(flags_cfg) else False
        elif isinstance(flags_cfg, dict):
            for h in self.ALL_HEADERS:
                old_flags[h] = bool(flags_cfg.get(h, False))

        # 新：为每一列创建独立的 bool 可见性属性（数据驱动，优先读新配置，回退 old_flags）
        # 买一/卖一共用 b1s1_visible：回退时两者任一为真即为真
        for header, attr in _HEADER_ATTRS.items():
            if attr == "b1s1_visible":
                fallback = old_flags.get("买一", False) or old_flags.get("卖一", False)
                val = bool(cfg.get(attr, fallback))
            else:
                val = bool(cfg.get(attr, old_flags.get(header, False)))
            setattr(self, attr, val)

        # 设置自选：股票/期货两套独立存储（向后兼容旧的单组 codes/checked_codes）
        # 旧配置只有一组 codes，按当时 market 归入对应市场组
        legacy_codes   = [str(c).strip() for c in codes_cfg if str(c).strip()]
        legacy_checked = [str(c).strip() for c in checked_codes_cfg if str(c).strip()]
        self.stock_codes   = list(cfg.get("stock_codes",   legacy_codes if self.market == "stock"   else ["sh000001"]))
        self.stock_checked = list(cfg.get("stock_checked", legacy_checked if self.market == "stock" else self.stock_codes))
        self.fut_codes     = list(cfg.get("fut_codes",     legacy_codes if self.market == "futures" else ["nf_RB2610"]))
        self.fut_checked   = list(cfg.get("fut_checked",   legacy_checked if self.market == "futures" else self.fut_codes))
        # 各组按自身市场过滤归一化
        self.stock_codes   = self._filter_codes_by_market(self.stock_codes, "stock")
        self.stock_checked = [c for c in self._filter_codes_by_market(self.stock_checked, "stock") if c in self.stock_codes]
        self.fut_codes     = self._filter_codes_by_market(self.fut_codes, "futures")
        self.fut_checked   = [c for c in self._filter_codes_by_market(self.fut_checked, "futures") if c in self.fut_codes]
        # self.codes / self.checked_codes 始终是当前市场那组的视图
        self.codes         = self.stock_codes   if self.market == "stock"   else self.fut_codes
        self.checked_codes = self.stock_checked if self.market == "stock"   else self.fut_checked
        self.font = QFont(font_family, max(8, min(15, font_size)))
        self.bg = QColor(bg["r"],bg["g"],bg["b"],bg["a"])
        
        
        self.hotkey_triggered.connect(self.toggle_win)
        self._register_hotkey()

        # UI
        self.panel = QWidget(self)
        self.panel.setObjectName("panel")
        self.vbox = QVBoxLayout(self.panel)
        self.vbox.setContentsMargins(10,6,10,6)
        self.vbox.setSpacing(0)

        self.table = QTableView(self.panel)
        self.table.setFrameShape(QFrame.NoFrame)
        self.table.setShowGrid(False)
        self.table.setSelectionMode(QAbstractItemView.NoSelection)
        self.table.setFocusPolicy(Qt.NoFocus)
        self.table.verticalHeader().setVisible(False)
        self.table.horizontalHeader().setVisible(self.header_visible)
        self.table.horizontalHeader().setStretchLastSection(False)
        # 列宽：允许手动拖拽调整（Interactive），用户调过的列宽会被记住
        self.table.horizontalHeader().setSectionResizeMode(QHeaderView.Interactive)
        self.table.horizontalHeader().setMinimumSectionSize(20)
        # 列宽记忆：列标题 -> 像素宽度（用户拖过的才记，未拖过的自动适应内容）
        self._col_widths = {}
        cw_cfg = cfg.get("col_widths", {})
        if isinstance(cw_cfg, dict):
            for k, v in cw_cfg.items():
                try:
                    self._col_widths[str(k)] = int(v)
                except (TypeError, ValueError):
                    pass
        self.table.setFont(self.font)
        self.table.horizontalHeader().setFont(self.font)
        self.table.verticalHeader().setMinimumSectionSize(1)
        self.table.verticalHeader().setDefaultSectionSize(1)
        self.table.horizontalHeader().setHighlightSections(False)
        # 列宽拖拽：用户手动调整过的列宽会被记录（按列标题），未调整的列自动适应内容
        self.table.horizontalHeader().sectionResized.connect(self._on_section_resized)
        self.table.setTextElideMode(Qt.ElideNone)
        self.error_label = QLabel("", self.panel)
        self.error_label.setStyleSheet("color: #ff6666; padding: 2px 4px;")
        self.error_label.setVisible(False)
        self.vbox.addWidget(self.error_label)

        self.model = SimpleTableModel(headers=self.ALL_HEADERS, align_right_cols=[1,2,3,4,5])
        self.model.set_color_scheme(self.default_color, self.fg)
        self.table.setModel(self.model)

        self.k_delegate = KLineDelegate(self.table, base_pt=12)
        self.k_delegate.update_scheme(self.default_color, self.fg)
        self.k_delegate.set_point_size(self.font.pointSize())
        self.k_column_visible_index = None

        self.vbox.addWidget(self.table)

        for w in (self.panel, self.table, self.table.viewport(), self.table.horizontalHeader(), self.table.verticalHeader()):
            w.installEventFilter(self)

        self.apply_style()
        self.set_window_opacity_percent(self.opacity_pct)
        self._fit_to_contents()

        scr = QApplication.primaryScreen().availableGeometry()
        pos = cfg.get("pos")
        if isinstance(pos, dict) and "x" in pos and "y" in pos:
            x, y = int(pos["x"]), int(pos["y"])
            x = max(scr.left(), min(x, scr.right()-self.width()))
            y = max(scr.top(),  min(y, scr.bottom()-self.height()))
            self.move(x, y)
        else:
            self.move(scr.right()-self.width()-40, scr.bottom()-self.height()-80)

        self._drag_pos = None

        self.timer = QTimer(self)
        self.timer.setInterval(int(max(0.2, self.refresh_seconds)*1000))
        self.timer.timeout.connect(self._refresh_from_function)
        self.timer.start()
        self._refresh_from_function()
        self._defer_fit()

        self._keep_top_timer = QTimer(self)
        self._keep_top_timer.setInterval(1000)  # 每 1000ms 检查一次
        self._keep_top_timer.timeout.connect(self._ensure_on_top)
        self._keep_top_timer.start()

    # 与 App 连接
    def set_open_settings_callback(self, fn):
        self._open_settings_cb = fn

    def set_quit_callback(self, fn):
        self._quit_cb = fn

    def set_on_change(self, fn): 
        self._on_change = fn or (lambda: None)

    def _notify_change(self):
        cb = getattr(self, "_on_change", None)
        if callable(cb): cb()

    def current_config(self):
        cfg = {
            "market": self.market,
            # 股票/期货两套独立自选，外加兼容旧键 codes/checked_codes(当前市场组)
            "stock_codes": self.stock_codes,
            "stock_checked": self.stock_checked,
            "fut_codes": self.fut_codes,
            "fut_checked": self.fut_checked,
            "codes": self.codes,
            "checked_codes": self.checked_codes,
            "short_code": self.short_code,
            "name_length": self.name_length,
            "fut_abbrev": self.fut_abbrev,
            "b1s1_price": (getattr(self, 'b1s1_display', 'qty') == 'price'),
            "b1s1_display": getattr(self, 'b1s1_display', 'qty'),
            "header_visible": self.header_visible,
            "grid_visible": self.grid_visible,
            "col_widths": dict(getattr(self, '_col_widths', {})),
            "refresh_seconds": self.refresh_seconds,
            "fg": self.fg.name(QColor.HexRgb),
            "bg": {"r": self.bg.red(), "g": self.bg.green(), "b": self.bg.blue(), "a": self.bg.alpha()},
            "opacity_pct": int(round(self.windowOpacity()*100)),
            "font_family": self.font.family(),
            "font_size": self.font.pointSize(),
            "line_extra_px": self.line_extra_px,
            "default_color": self.default_color,
            "pos": {"x": self.x(), "y": self.y()},
            "hotkey": self.hotkey,
            "start_on_boot": bool(self.start_on_boot),
        }
        # 列可见性（数据驱动写入，属性名即配置键名）
        for attr in set(_HEADER_ATTRS.values()):
            cfg[attr] = bool(getattr(self, attr, False))
        return cfg

    def header_is_visible(self, header: str) -> bool:
        """返回指定列标题对应的可见性属性值（数据驱动查表）。"""
        attr = _HEADER_ATTRS.get(header)
        return bool(getattr(self, attr, False)) if attr else False

    # ----- 外观/尺寸 -----
    def apply_style(self):
        r,g,b,a = self.bg.red(), self.bg.green(), self.bg.blue(), self.bg.alpha()
        fg_r, fg_g, fg_b = self.fg.red(), self.fg.green(), self.fg.blue()
        line_col = f"rgba({fg_r},{fg_g},{fg_b},80)"
        self.panel.setStyleSheet(f"""
            QWidget#panel {{
                background: rgba({r},{g},{b},{a});
                border-radius: 5px;
            }}
            QTableView {{
                background: transparent;
                border: {f"1px solid {line_col}" if self.grid_visible else "none"};
                border-radius: 3px;
                {"" if self.default_color else f"color: {self.fg.name()};"}
                outline: none;
            }}
            QTableView::item {{
                border-right: {f"1px solid {line_col}" if self.grid_visible else "none"};
                border-bottom: {f"1px solid {line_col}" if self.grid_visible else "none"};
            }}
            QHeaderView {{
                background-color: transparent;
            }}
            QHeaderView::section {{
                background: transparent;
                border: none;
                border-bottom: 1px solid {line_col};
                font-weight: 600;
                {"" if self.default_color else f"color: {self.fg.name()};"}
                padding: 2px 4px;
            }}
        """)
        self.table.setFont(self.font)
        self.table.horizontalHeader().setFont(self.font)
        self._defer_fit()

    def _apply_row_heights(self):
        fm = self.table.fontMetrics()
        h = fm.height() + max(0, self.line_extra_px)
        self.table.verticalHeader().setDefaultSectionSize(h)
        for r in range(self.model.rowCount()):
            self.table.setRowHeight(r, h)

    def _current_headers(self):
        """当前 model 的列标题列表（按当前显示列顺序，返回 str）。"""
        return [str(self.model.headerData(c, Qt.Horizontal)) for c in range(self.model.columnCount())]

    def _on_section_resized(self, logical_idx, old_size, new_size):
        """用户拖动列分隔线时，记录该列宽（按列标题）。程序设宽时不记录。"""
        if getattr(self, "_self_resizing", False):
            return
        headers = self._current_headers()
        if 0 <= logical_idx < len(headers):
            self._col_widths[headers[logical_idx]] = int(new_size)

    def _fit_to_contents(self):
        self.table.horizontalHeader().setStretchLastSection(False)
        # 先按内容自动适应所有列宽
        self._self_resizing = True
        try:
            self.table.resizeColumnsToContents()
            # 再覆盖：已锁定（用户调过）的列恢复为记录宽度
            headers = self._current_headers()
            for c, h in enumerate(headers):
                if h in self._col_widths:
                    self.table.setColumnWidth(c, self._col_widths[h])
        finally:
            self._self_resizing = False
        self._apply_row_heights()

        cols = self.model.columnCount()
        rows = self.model.rowCount()
        total_w = self.table.verticalHeader().width() + 2*self.table.frameWidth()
        for c in range(cols):
            total_w += self.table.columnWidth(c)
        hh = self.table.horizontalHeader().height() if self.table.horizontalHeader().isVisible() else 0
        total_h = hh + 2*self.table.frameWidth()
        for r in range(rows):
            total_h += self.table.rowHeight(r)
        self.table.setFixedSize(max(1,total_w), max(1,total_h))
        self.panel.adjustSize()
        self.resize(self.panel.size())

    def _defer_fit(self):
        QTimer.singleShot(0, self._fit_to_contents)

    # ----- 数据 & 投影 -----
    def _show_error(self, msg):
        try:
            if self.k_column_visible_index is not None:
                self.table.setItemDelegateForColumn(self.k_column_visible_index, QStyledItemDelegate(self.table))
                self.k_column_visible_index = None
        except Exception:
            pass
        # 若是 requests 抛出的网络错误，显示更友好的中文提示
        if isinstance(msg, requests.exceptions.RequestException):
            text = "无网络连接"
        else:
            text = str(msg) if msg is not None else ""

        if hasattr(self, 'error_label'):
            self.error_label.setText(text)
            self.error_label.setVisible(True)
        self._defer_fit()

    def _clear_error(self):
        # 清除顶部错误提示
        if hasattr(self, 'error_label'):
            self.error_label.setVisible(False)
            self.error_label.setText("")

    # ----- 数据来源：新浪财经 -----
    def _get_price(self, codes:list):
        # 行情抓取与解析已抽离至 data/futures_data，此处为薄包装，保持调用接口不变
        if self.market == "futures":
            opts = {
                "short_code": self.short_code,
                "name_length": self.name_length,
                "abbrev": self.fut_abbrev,
            }
            return fetch_futures(codes, opts)
        opts = {
            "short_code": self.short_code,
            "name_length": self.name_length,
            "b1s1_display": getattr(self, 'b1s1_display', 'qty'),
        }
        return fetch_quotes(codes, opts)

    def _project_columns(self, full_rows, sign_data):
        # 从 ALL_HEADERS 中按显示顺序筛选已启用的列
        cols = [i for i, h in enumerate(self.ALL_HEADERS) if self.header_is_visible(h)]
        headers = [self.ALL_HEADERS[i] for i in cols]

        proj_rows, proj_meta = [], []
        for r, row in enumerate(full_rows):
            proj_rows.append([row[i] for i in cols])
            proj_meta.append(sign_data[r])

        # 数值列按小数点对齐（补空格，使同列小数点上下对齐）
        self._align_decimals(proj_rows, headers)

        # 右对齐：除了名称、K线、卖一外的所有列都右对齐
        right_cols = [i for i, h in enumerate(headers) if h not in ("名称", "K线", "卖一")]
        self.model.set_align_right_cols(right_cols)
        # model 重置会触发 QHeaderView 自动重算列宽并发出 sectionResized；
        # 用 _self_resizing 包裹整个流程，避免误判为用户拖动
        self._self_resizing = True
        try:
            self.model.set_rows_headers(proj_rows, headers, meta=proj_meta)
            self.model.set_color_scheme(self.default_color, self.fg)

            if "K线" in headers:
                col = headers.index("K线")
                self.k_column_visible_index = col
                self.k_delegate.update_scheme(self.default_color, self.fg)
                self.k_delegate.set_point_size(self.font.pointSize())
                self.table.setItemDelegateForColumn(col, self.k_delegate)
            else:
                if self.k_column_visible_index is not None:
                    self.table.setItemDelegateForColumn(self.k_column_visible_index, QStyledItemDelegate(self.table))
                    self.k_column_visible_index = None
        finally:
            self._self_resizing = False

        self._fit_to_contents()

    def _refresh_from_function(self):
        try:
            full_rows, sign = self._get_price(self.checked_codes)
        except Exception as e:
            self._show_error(e)
            return
        self._clear_error()
        self._project_columns(full_rows, sign)

    # ----- 应用设置 -----
    def set_codes(self, codes_list):
        # 按当前市场过滤并归一化（期货合约字母大写），去重
        new = self._filter_codes_by_market(codes_list, self.market)
        if not new:
            new = ["nf_RB2610"] if self.market == "futures" else ["sh000001"]
        self.codes = new
        # 同步写入对应市场组的独立存储
        if self.market == "stock":
            self.stock_codes = list(new)
        else:
            self.fut_codes = list(new)
        self._notify_change()
        self._refresh_from_function()

    def set_checked_codes(self, codes_list):
        # 按当前市场过滤并归一化，去重
        new = self._filter_codes_by_market(codes_list, self.market)
        if not new:
            new = ["nf_RB2610"] if self.market == "futures" else ["sh000001"]
        self.checked_codes = new
        # 同步写入对应市场组的独立存储
        if self.market == "stock":
            self.stock_checked = list(new)
        else:
            self.fut_checked = list(new)
        self._notify_change()
        self._refresh_from_function()

    def set_flag(self, idx, checked: bool):
        """设置指标显示标志。idx 可以是整数索引（向后兼容）或列标题字符串"""
        # 兼容老版本：若传整数索引，转为列标题
        if isinstance(idx, int):
            header = self.ALL_HEADERS[idx] if 0 <= idx < len(self.ALL_HEADERS) else None
        else:
            header = str(idx) if str(idx) in self.ALL_HEADERS else None
        if header is None:
            return

        checked = bool(checked)
        attr = _HEADER_ATTRS.get(header)
        if not attr:
            return
        prev = bool(getattr(self, attr, False))
        setattr(self, attr, checked)

        # 状态未变化则跳过刷新（避免额外刷新）
        if prev == checked:
            return
        self._notify_change()
        self._refresh_from_function()

    def set_code_type(self, pure_num: bool):
        self.short_code = bool(pure_num)
        self._notify_change()
        self._refresh_from_function()

    def set_name_length(self, name_len: int):
        if name_len >=0:
            self.name_length = name_len
            self._notify_change()
            self._refresh_from_function()

    def set_fut_abbrev(self, enabled: bool):
        """期货名称用品种缩写（螺纹钢2610→RB2610）。仅期货模式生效。"""
        self.fut_abbrev = bool(enabled)
        self._notify_change()
        self._refresh_from_function()

    def set_b1s1_display(self, mode: str):
        """mode: 'qty' | 'price' | 'both'"""
        if mode not in ("qty", "price", "both"):
            return
        self.b1s1_display = mode
        self._notify_change()
        self._refresh_from_function()

    def set_header_visible(self, vis: bool):
        self.header_visible = bool(vis)
        self.table.horizontalHeader().setVisible(self.header_visible)
        self._notify_change()
        self._defer_fit()

    def reset_col_widths(self):
        """清空列宽记忆，恢复全部列按内容自动适应。"""
        self._col_widths = {}
        self._notify_change()
        self._fit_to_contents()

    def set_grid_visible(self, vis: bool):
        self.grid_visible = bool(vis)
        self.apply_style()
        self._notify_change()

    def set_refresh_interval(self, seconds):
        # 支持亚秒(0.2/0.3/0.5)，最小 0.2 秒，避免过频请求被接口限流
        try:
            seconds = float(seconds)
        except (TypeError, ValueError):
            return
        if seconds < 0.2:
            seconds = 0.2
        self.refresh_seconds = seconds
        self.timer.setInterval(int(seconds*1000))
        self._notify_change()

    def set_fg_color(self, c: QColor):
        if isinstance(c, QColor) and c.isValid():
            self.fg = QColor(c)
            self.apply_style()
            self._notify_change()

    def set_bg_rgb_keep_alpha(self, c: QColor):
        if isinstance(c, QColor) and c.isValid():
            c2 = QColor(c)
            c2.setAlpha(self.bg.alpha())
            self.bg = c2
            self.apply_style()
            self._notify_change()

    def set_bg_alpha_percent(self, percent_0_100: int):
        p = max(0, min(100, int(percent_0_100)))
        self.bg.setAlpha(int(round(p*2.55)))
        self.apply_style()
        self._notify_change()

    def set_window_opacity_percent(self, percent_20_100: int):
        p = max(20, min(100, int(percent_20_100)))
        self.setWindowOpacity(p/100.0)
        self._defer_fit()
        self._notify_change()

    def set_font_size(self, pt: int):
        pt = max(8, min(15, int(pt)))
        self.font.setPointSize(pt)
        self.k_delegate.set_point_size(pt)
        self.apply_style()
        self._notify_change()
        self.table.viewport().update()
        self._defer_fit()

    def set_font_family(self, family: str):
        if family and family != self.font.family():
            self.font.setFamily(family)
            self.apply_style()
            self._notify_change()

    def set_line_extra(self, px: int):
        self.line_extra_px = max(0, int(px))
        self.apply_style()
        self._defer_fit()
        self._notify_change()

    def set_default_color(self, enabled: bool):
        self.default_color = bool(enabled)
        self.model.set_color_scheme(self.default_color, self.fg)
        self.k_delegate.update_scheme(self.default_color, self.fg)
        self.apply_style()
        self._notify_change()
        self._defer_fit()

    def set_start_on_boot(self, enabled: bool):
        self.start_on_boot = bool(enabled)
        self._notify_change()

    @staticmethod
    def _filter_codes_by_market(codes, market):
        """按指定市场过滤代码格式，剔除跨市场残留与失效前缀(hf_/dce_/czce_ 等)。
        stock: sh/sz/bj + 6位数字；futures: nf_ + 合约。
        期货代码会归一化合约字母为大写（接口要求 nf_RB2610 而非 nf_rb2610）。"""
        pat = _RE_FUTURES if market == "futures" else _RE_STOCK
        out, seen = [], set()
        for c in codes:
            c = str(c).strip()
            if not c or c in seen:
                continue
            if not pat.match(c):
                continue
            if market == "futures":
                # 归一化：nf_ 小写 + 合约大写
                c = "nf_" + c[3:].upper()
            seen.add(c)
            out.append(c)
        return out

    def set_market(self, mode: str):
        """切换市场：'stock'(股票) / 'futures'(国内期货)。
        股票/期货自选各自独立保存，切换时切换到对应那组列表并刷新。"""
        if mode not in ("stock", "futures") or mode == self.market:
            return
        self.market = mode
        # 切换到目标市场的独立自选列表
        if mode == "stock":
            self.codes = list(self.stock_codes)
            self.checked_codes = list(self.stock_checked)
        else:
            self.codes = list(self.fut_codes)
            self.checked_codes = list(self.fut_checked)
        self._notify_change()
        self._refresh_from_function()

    # ----- 交互 -----
    def contextMenuEvent(self, event):
        menu = QMenu(self)
        sub_cols = QMenu("显示指标", menu)
        for name in self.ALL_HEADERS:
            if name == "卖一":
                continue  # 买一/卖一合并为一个开关
            label = "买一/卖一" if name == "买一" else name
            act = QAction(label, sub_cols, checkable=True)
            act.setChecked(self.header_is_visible(name))
            act.toggled.connect(partial(self.set_flag, name))
            # 期货模式禁用不支持的数据列（盘口/委比/成交量/成交额/均价）
            if self.market == "futures" and name in _FUTURES_UNSUPPORTED:
                act.setEnabled(False)
            sub_cols.addAction(act)
        menu.addMenu(sub_cols)

        act_header = QAction("显示表头", menu, checkable=True)
        act_header.setChecked(self.header_visible)
        act_header.toggled.connect(self.set_header_visible)
        menu.addAction(act_header)

        act_reset_width = QAction("重置列宽", menu)
        act_reset_width.triggered.connect(self.reset_col_widths)
        # 没有手动调过任何列宽时，该项灰显
        act_reset_width.setEnabled(bool(getattr(self, '_col_widths', {})))
        menu.addAction(act_reset_width)

        act_grid = QAction("显示网格",menu, checkable=True)
        act_grid.setChecked(self.grid_visible)
        act_grid.toggled.connect(self.set_grid_visible)
        menu.addAction(act_grid)

        act_color = QAction("默认颜色", menu, checkable=True)
        act_color.setChecked(self.default_color)
        act_color.toggled.connect(self.set_default_color)
        menu.addAction(act_color)

        menu.addSeparator()
        act_open_settings = QAction("设置…", menu)
        act_open_settings.triggered.connect(self._open_settings_cb)
        menu.addAction(act_open_settings)

        menu.addSeparator()
        menu.addAction(QAction("隐藏浮窗", menu, triggered=self.hide))
        if callable(getattr(self, '_quit_cb', None)):
            menu.addAction(QAction("退出", menu, triggered=self._quit_cb))
        menu.exec(event.globalPos())

    def _begin_drag(self, ev):
        """开始拖拽：记录按下点。窗口本体事件与子控件 eventFilter 共用。"""
        self._drag_pos = ev.globalPosition().toPoint() - self.frameGeometry().topLeft()
        self.setFocus(Qt.MouseFocusReason)

    def _do_drag(self, ev):
        """拖拽中：随光标移动窗口。"""
        if getattr(self, "_drag_pos", None):
            self.move(ev.globalPosition().toPoint() - self._drag_pos)
            self._ensure_on_top()

    def _end_drag(self):
        """结束拖拽：清空拖拽状态并保存位置。"""
        self._drag_pos = None
        self._ensure_on_top()
        self._notify_change()

    def mousePressEvent(self, e):
        if e.button() == Qt.LeftButton:
            self._begin_drag(e)

    def mouseMoveEvent(self, e):
        if getattr(self, "_drag_pos", None) and (e.buttons() & Qt.LeftButton):
            self._do_drag(e)

    def mouseReleaseEvent(self, e):
        if e.button() == Qt.LeftButton:
            self._end_drag()

    def mouseDoubleClickEvent(self, e):
        if e.button() == Qt.LeftButton:
            self._drag_pos = None
            self.hide()

    def eventFilter(self, obj, ev):
        if ev.type() == QEvent.MouseButtonDblClick and hasattr(ev, "button") and ev.button() == Qt.LeftButton:
            self._drag_pos = None
            self.hide()
            return True
        if ev.type() == QEvent.MouseButtonPress and hasattr(ev, "button") and ev.button() == Qt.LeftButton:
            self._begin_drag(ev)
            return True
        if ev.type() == QEvent.MouseMove and hasattr(ev, "buttons") and (ev.buttons() & Qt.LeftButton) and getattr(self, "_drag_pos", None):
            self._do_drag(ev)
            return True
        if ev.type() == QEvent.MouseButtonRelease and hasattr(ev, "button") and ev.button() == Qt.LeftButton:
            self._end_drag()
            return True
        return QWidget.eventFilter(self, obj, ev)

    def closeEvent(self, event): 
        event.ignore()
        self.hide()

    def showEvent(self, event):
        super().showEvent(event)
        if self.timer and not self.timer.isActive(): 
            self.timer.start()
        if self._keep_top_timer and not self._keep_top_timer.isActive():
            self._keep_top_timer.start()
        self._defer_fit()

    def hideEvent(self, event):
        super().hideEvent(event)
        if self.timer and self.timer.isActive(): 
            self.timer.stop()
        if self._keep_top_timer and self._keep_top_timer.isActive():
            self._keep_top_timer.stop()

    def _ensure_on_top(self):
        if not self.isVisible():
            return
        try:
            aw = QApplication.activeWindow()
            popup = QApplication.activePopupWidget()
            if aw is not None and aw is not self and not self.isAncestorOf(aw):
                return
            if popup is not None and popup is not self and not self.isAncestorOf(popup):
                return
        except Exception:
            pass
        self.raise_()

    def _register_hotkey(self):
        try:
            keyboard.remove_all_hotkeys()
        except Exception:
            pass
        keyboard.add_hotkey(self.hotkey.lower(), lambda: self.hotkey_triggered.emit())

    def update_hotkey(self, new_hotkey: str):
        self.hotkey = new_hotkey.strip()
        self._register_hotkey()

    def toggle_win(self):
        if self.isVisible():
            self.hide()
        else:
            self.show()