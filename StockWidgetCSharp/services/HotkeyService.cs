using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace StockWidget;

/// <summary>
/// 全局热键（对应 Python keyboard 库）。
/// 用 P/Invoke RegisterHotKey + HwndSource 拦截 WM_HOTKEY。
/// 支持热键字符串解析（如 "Ctrl+Alt+F"）。
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 9001;

    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _registered;
    private bool _ownsSource;
    private string _current = "";

    public event Action? Triggered;

    public void EnsureHooked(IntPtr ownerHwnd = default)
    {
        if (_source != null) return;
        _hwnd = ownerHwnd;
        if (_hwnd != IntPtr.Zero)
        {
            _source = HwndSource.FromHwnd(_hwnd);
            _ownsSource = false;
        }
        else
        {
            var p = new HwndSourceParameters("StockWidgetHotkeySink")
            {
                Width = 0,
                Height = 0,
                WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            };
            _source = new HwndSource(p);
            _hwnd = _source.Handle;
            _ownsSource = true;
        }
        _source?.AddHook(WndProc);
    }

    /// <summary>注册全局热键。hotkey 形如 "Ctrl+Alt+F"。返回 false 表示格式或注册失败。</summary>
    public bool Register(string hotkey)
    {
        Unregister();
        hotkey = hotkey.Trim();
        _current = hotkey;
        if (string.IsNullOrEmpty(hotkey)) return false;

        EnsureHooked();
        if (_source == null || _hwnd == IntPtr.Zero) return false;

        var (mods, vk) = ParseHotkey(hotkey);
        if (vk == 0) return false;
        _registered = RegisterHotKey(_hwnd, HotkeyId, mods, (uint)vk);
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Triggered?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Unregister()
    {
        if (_registered && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HotkeyId);
            _registered = false;
        }
    }

    /// <summary>解析 "Ctrl+Alt+F" -> (modifiers, vk)。</summary>
    public static (uint mods, uint vk) ParseHotkey(string hotkey)
    {
        uint mods = 0;
        uint vk = 0;
        foreach (var part in hotkey.Split('+'))
        {
            var p = part.Trim().ToLowerInvariant();
            switch (p)
            {
                case "ctrl": case "control": mods |= 0x0002; break;
                case "alt": mods |= 0x0001; break;
                case "shift": mods |= 0x0004; break;
                case "win": mods |= 0x0008; break;
                default:
                    // 单字符（F、A、1）或功能键（F1-F24）
                    if (p.Length == 1) vk = (uint)p.ToUpperInvariant()[0];
                    else if (p.StartsWith("f") && int.TryParse(p[1..], out var fn) && fn is >= 1 and <= 24)
                        vk = (uint)(Keys.F1 + fn - 1);
                    break;
            }
        }
        return (mods, vk);
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        if (_ownsSource) _source?.Dispose();
        _source = null;
    }

    // Windows virtual key codes (子集)
    private enum Keys
    {
        F1 = 0x70, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
    }
}
