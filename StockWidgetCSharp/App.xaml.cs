using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace StockWidget;

/// <summary>
/// 应用入口：加载配置 → 启动浮窗 → 托盘 → 全局热键 → 配置保存。
/// </summary>
public partial class App : Application
{
    [DllImport("shell32.dll")]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    private AppConfig? _cfg;
    private FloatWindow? _win;
    private SettingsDialog? _settingsDlg;
    private HotkeyService? _hotkey;
    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Forms.ToolStripMenuItem? _trayMouseThroughItem;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // AppUserModelID（任务栏图标分组）
        try { SetCurrentProcessExplicitAppUserModelID("StockWidget.1"); } catch { }
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // 加载配置
        var raw = Config.Load();
        _cfg = AppConfig.FromJson(raw);

        // 启动浮窗
        _win = new FloatWindow(_cfg)
        {
            Changed = SaveNow,
            RequestSettings = OpenSettings,
            RequestQuit = QuitApp,
        };
        _win.Show();
        PositionWindow(_win, _cfg);
        ApplyStartOnBoot(_cfg.StartOnBoot);

        // 全局热键
        _hotkey = new HotkeyService();
        _hotkey.Triggered += () => Dispatcher.Invoke(() => _win.ToggleVisibility());
        // 浮窗句柄创建后注册热键
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var hwnd = new WindowInteropHelper(_win).Handle;
            _hotkey.EnsureHooked(hwnd);
            _hotkey.Register(_cfg.Hotkey);
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // 托盘
        SetupTray();

        // 延迟首次保存
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        t.Tick += (s, e) => { SaveNow(); t.Stop(); };
        t.Start();
    }

    private void SetupTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "StockWidget",
            Visible = true,
        };
        ApplyTrayIcon();
        var menu = new System.Windows.Forms.ContextMenuStrip
        {
            BackColor = System.Drawing.Color.White,
            ForeColor = System.Drawing.Color.FromArgb(32, 32, 32),
            Font = new System.Drawing.Font("Microsoft YaHei UI", 8.5F),
            Padding = new System.Windows.Forms.Padding(3, 2, 3, 2),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            DropShadowEnabled = true,
            Renderer = new ModernToolStripRenderer()
        };
        menu.Opening += (s, e) =>
        {
            if (_trayMouseThroughItem != null && _cfg != null)
                _trayMouseThroughItem.Checked = _cfg.MouseThroughEnabled;
        };
        menu.Items.Add("显示/隐藏 浮窗", null, (s, e) => _win!.ToggleVisibility());
        _trayMouseThroughItem = new System.Windows.Forms.ToolStripMenuItem("鼠标穿透")
        {
            CheckOnClick = true,
            Checked = _cfg?.MouseThroughEnabled == true
        };
        _trayMouseThroughItem.CheckedChanged += (s, e) =>
        {
            if (_cfg == null || _win == null || _trayMouseThroughItem == null) return;
            if (_cfg.MouseThroughEnabled == _trayMouseThroughItem.Checked) return;
            _cfg.MouseThroughEnabled = _trayMouseThroughItem.Checked;
            _win.ApplyConfigChanges();
            SaveNow();
        };
        menu.Items.Add(_trayMouseThroughItem);
        menu.Items.Add("设置…", null, (s, e) => OpenSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (s, e) => QuitApp());
        foreach (System.Windows.Forms.ToolStripItem item in menu.Items)
        {
            if (item is System.Windows.Forms.ToolStripSeparator)
            {
                item.Margin = new System.Windows.Forms.Padding(5, 2, 5, 2);
            }
            else
            {
                item.Padding = new System.Windows.Forms.Padding(7, 3, 10, 3);
                item.Margin = new System.Windows.Forms.Padding(0);
            }
        }
        menu.Opened += (s, e) => ApplyRoundedMenuRegion(menu);
        menu.SizeChanged += (s, e) => ApplyRoundedMenuRegion(menu);
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (s, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
                _win!.ToggleVisibility();
        };
    }

    private void ApplyTrayIcon()
    {
        if (_tray == null) return;
        var icon = ResolveIcon(_cfg?.AppIcon);
        _tray.Icon = icon;
        // WPF 窗口图标用 ImageSource（从 ico 转换）
        if (_win != null)
        {
            try
            {
                var bm = icon.ToBitmap();
                var ms = new System.IO.MemoryStream();
                bm.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                _win.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(ms);
            }
            catch { }
        }
    }

    private static System.Drawing.Icon ResolveIcon(string? choice)
    {
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var defaultPath = System.IO.Path.Combine(exeDir, "StockWidget.ico");
        try
        {
            if (string.IsNullOrEmpty(choice) || choice == "default")
            {
                if (System.IO.File.Exists(defaultPath))
                    return new System.Drawing.Icon(defaultPath);
            }
            else if (System.IO.File.Exists(choice))
            {
                return new System.Drawing.Icon(choice);
            }
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    private void OpenSettings()
    {
        if (_settingsDlg != null && _settingsDlg.IsVisible)
        {
            _settingsDlg.Activate();
            return;
        }
        if (_cfg == null || _win == null) return;
        _settingsDlg = new SettingsDialog(_cfg, _win, SaveNow, SetAppIcon, TrySetHotkey) { Owner = null };
        _settingsDlg.Show();
    }

    private void SetAppIcon(string? choice)
    {
        _cfg!.AppIcon = choice;
        ApplyTrayIcon();
    }

    private bool TrySetHotkey(string hotkey)
    {
        if (_cfg == null || _hotkey == null) return false;
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            _hotkey.Unregister();
            _cfg.Hotkey = "";
            return true;
        }
        var old = _cfg.Hotkey;
        if (_hotkey.Register(hotkey))
        {
            _cfg.Hotkey = hotkey;
            return true;
        }
        _hotkey.Register(old);
        return false;
    }

    private static void ApplyStartOnBoot(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            if (enabled)
            {
                string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exe)) key.SetValue("StockWidget", $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue("StockWidget", false);
            }
        }
        catch { }
    }

    private void PositionWindow(FloatWindow win, AppConfig cfg)
    {
        var screen = SystemParameters.WorkArea;
        var width = double.IsFinite(win.ActualWidth) && win.ActualWidth > 0 ? win.ActualWidth : 1;
        var height = double.IsFinite(win.ActualHeight) && win.ActualHeight > 0 ? win.ActualHeight : 1;
        if (cfg.HasPos && double.IsFinite(cfg.PosX) && double.IsFinite(cfg.PosY))
        {
            win.Left = cfg.PosX;
            win.Top = cfg.PosY;
        }
        else
        {
            win.Left = screen.Right - width - 40;
            win.Top = screen.Bottom - height - 80;
        }
        win.EnsureVisible();
    }

    private void SaveNow()
    {
        if (_cfg == null || _win == null) return;
        if (double.IsFinite(_win.Left) && double.IsFinite(_win.Top))
        {
            _cfg.PosX = _win.Left;
            _cfg.PosY = _win.Top;
            _cfg.HasPos = true;
        }
        Config.Save(_cfg.ToJson());
    }

    private void QuitApp()
    {
        SaveNow();
        _hotkey?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Shutdown();
    }

    private static void ApplyRoundedMenuRegion(System.Windows.Forms.ContextMenuStrip menu)
    {
        if (menu.Width <= 1 || menu.Height <= 1) return;
        using var path = CreateRoundedPath(new System.Drawing.Rectangle(0, 0, menu.Width, menu.Height), 8);
        var previous = menu.Region;
        menu.Region = new System.Drawing.Region(path);
        previous?.Dispose();
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedPath(System.Drawing.Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        var arc = new System.Drawing.Rectangle(bounds.Location, new System.Drawing.Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter - 1;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter - 1;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class ModernToolStripRenderer : System.Windows.Forms.ToolStripProfessionalRenderer
    {
        public ModernToolStripRenderer() : base(new ModernMenuColorTable())
        {
            RoundedEdges = true;
        }

        protected override void OnRenderMenuItemBackground(System.Windows.Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var bounds = new System.Drawing.Rectangle(3, 1, Math.Max(1, e.Item.Width - 6), Math.Max(1, e.Item.Height - 2));
            using var path = CreateRoundedPath(bounds, 5);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(242, 242, 242));
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderSeparator(System.Windows.Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(232, 232, 232));
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 8, y, Math.Max(8, e.Item.Width - 8), y);
        }

        protected override void OnRenderToolStripBorder(System.Windows.Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = CreateRoundedPath(new System.Drawing.Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 8);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(218, 218, 218));
            e.Graphics.DrawPath(pen, path);
        }
    }

    private sealed class ModernMenuColorTable : System.Windows.Forms.ProfessionalColorTable
    {
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.White;
        public override System.Drawing.Color MenuBorder => System.Drawing.Color.FromArgb(218, 218, 218);
        public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.Transparent;
        public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(242, 242, 242);
        public override System.Drawing.Color ImageMarginGradientBegin => System.Drawing.Color.White;
        public override System.Drawing.Color ImageMarginGradientMiddle => System.Drawing.Color.White;
        public override System.Drawing.Color ImageMarginGradientEnd => System.Drawing.Color.White;
        public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(232, 232, 232);
        public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(232, 232, 232);
        public override System.Drawing.Color CheckBackground => System.Drawing.Color.FromArgb(232, 241, 255);
        public override System.Drawing.Color CheckSelectedBackground => System.Drawing.Color.FromArgb(222, 235, 255);
    }
}
