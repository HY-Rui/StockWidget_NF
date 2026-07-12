using System.Windows;
using System.Windows.Media;

namespace StockWidget;

/// <summary>
/// 当日 K 线自绘控件（对应 Python Display.KLineDelegate）。
/// 基于五元组 (开盘, 现价, 最高, 最低, 昨收) 绘制：昨收虚线 + 实体 + 上下影线。
/// 颜色：默认色模式按开收关系（涨红/跌绿/平灰）；非默认色用前景色。
/// </summary>
internal sealed class KLineControl : FrameworkElement
{
    private (double O, double C, double H, double L, double P)? _k;
    private bool _defaultColor;
    private Color _fg = Colors.White;
    private double _scale = 1.0;

    private static readonly Color UpColor = (Color)ColorConverter.ConvertFromString(Constants.UpColor);
    private static readonly Color DownColor = (Color)ColorConverter.ConvertFromString(Constants.DownColor);
    private static readonly Color NeutralColor = (Color)ColorConverter.ConvertFromString(Constants.NeutralColor);

    // 字号→缩放（base 12pt，0.5~1.5）
    public void SetPointSize(int pt) => _scale = Math.Clamp((double)pt / 12.0, 0.5, 1.5);

    public void UpdateScheme(bool defaultColor, Color fg)
    {
        _defaultColor = defaultColor;
        _fg = fg;
    }

    public void SetData((double O, double C, double H, double L, double P)? k)
    {
        _k = k;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var rect = new Rect(1, 1, Math.Max(2, ActualWidth - 2), Math.Max(2, ActualHeight - 2));
        if (_k is not { } k || !IsValid(k))
        {
            DrawEmpty(dc, rect);
            return;
        }

        double o = k.O, c = k.C, h = k.H, l = k.L, p = k.P;
        if (h < l) (h, l) = (l, h);
        h = Math.Max(h, Math.Max(Math.Max(o, c), p));
        l = Math.Min(l, Math.Min(Math.Min(o, c), p));

        double sc = Math.Clamp(_scale, 0.5, 1.5);
        double vpad = Math.Max(2, rect.Height * (0.12 + 0.06 * (sc - 1)));
        double hEff = Math.Max(2, rect.Height - 2 * vpad);
        var krect = new Rect(rect.X, rect.Y + vpad, rect.Width, hEff);

        double yFor(double v)
        {
            double y;
            if (h == l && l == p) y = 0.5;
            else y = (v - Math.Min(l, p)) / (Math.Max(h, p) - Math.Min(l, p));
            return krect.Y + (1 - y) * krect.Height;
        }

        double yO = yFor(o), yC = yFor(c), yH = yFor(h), yL = yFor(l), yP = yFor(p);
        double bodyW = Math.Clamp(krect.Width * 0.46 * sc, 6, 14);
        double x = krect.X + krect.Width / 2;

        // 昨收虚线
        var dashCol = _defaultColor ? NeutralColor : _fg;
        dashCol = Color.FromArgb(150, dashCol.R, dashCol.G, dashCol.B);
        var dashPen = new Pen(new SolidColorBrush(dashCol), 1) { DashStyle = DashStyles.Dash };
        dc.DrawLine(dashPen, new Point(krect.Left, yP), new Point(krect.Right, yP));

        Color kcolor = _fg;
        if (_defaultColor)
        {
            kcolor = c > p ? UpColor : (c < p ? DownColor : NeutralColor);
        }
        var kBrush = new SolidColorBrush(kcolor);
        var kPen = new Pen(kBrush, 1);

        double top = Math.Min(yO, yC), bot = Math.Max(yO, yC);
        double bodyH = Math.Max(1.5, bot - top);
        double bodyX = x - bodyW / 2;

        if (Math.Abs(c - o) > double.Epsilon)
        {
            var fill = c < o ? kBrush : null;
            dc.DrawRectangle(fill, kPen, new Rect(bodyX, top, bodyW, bodyH));
        }
        else
        {
            dc.DrawLine(kPen, new Point(bodyX, yC), new Point(bodyX + bodyW, yC));
        }

        if (yH < top) dc.DrawLine(kPen, new Point(x, yH), new Point(x, top));
        if (yL > bot) dc.DrawLine(kPen, new Point(x, bot), new Point(x, yL));
    }

    private void DrawEmpty(DrawingContext dc, Rect rect)
    {
        var col = _defaultColor ? NeutralColor : _fg;
        col = Color.FromArgb(150, col.R, col.G, col.B);
        var pen = new Pen(new SolidColorBrush(col), 1);
        var y = rect.Top + rect.Height / 2;
        dc.DrawLine(pen, new Point(rect.Left + 4, y), new Point(rect.Right - 4, y));
    }

    private static bool IsValid((double O, double C, double H, double L, double P) k)
    {
        return IsPrice(k.O) && IsPrice(k.C) && IsPrice(k.H) && IsPrice(k.L) && IsPrice(k.P)
            && (k.O > 0 || k.C > 0 || k.H > 0 || k.L > 0 || k.P > 0);
    }

    private static bool IsPrice(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v >= 0;
}
