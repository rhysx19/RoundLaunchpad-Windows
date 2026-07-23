using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RoundLaunchpad.Services;

/// <summary>
/// Global Alt+Space hotkey (Mac ⌥Space parity). Reports press and release so
/// hold-and-drag launching works. Optional double-tap Alt via a low-level hook.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    public const int HotkeyId = 0x524C; // 'RL'

    private readonly Action _onDown;
    private readonly Action _onUp;
    private readonly Action _onDoubleTapAlt;
    private HwndSource? _source;
    private bool _registered;
    private bool _spaceDown;
    private bool _altDown;
    private DispatcherTimer? _releasePoll;
    private IntPtr _hook = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private double _lastAltPressUptime;
    private bool _doubleTapEnabled;

    public HotkeyService(Action onDown, Action onUp, Action onDoubleTapAlt)
    {
        _onDown = onDown;
        _onUp = onUp;
        _onDoubleTapAlt = onDoubleTapAlt;
    }

    public void Attach(IntPtr hwnd)
    {
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);

        // MOD_ALT | MOD_NOREPEAT, VK_SPACE
        _registered = RegisterHotKey(hwnd, HotkeyId, MOD_ALT | MOD_NOREPEAT, VK_SPACE);
        if (!_registered)
        {
            // Fallback: Ctrl+Alt+Space if Alt+Space is taken
            _registered = RegisterHotKey(hwnd, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_SPACE);
        }

        _releasePoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _releasePoll.Tick += (_, _) => PollRelease();
    }

    public void SetDoubleTapAlt(bool enabled)
    {
        _doubleTapEnabled = enabled;
        if (enabled) InstallHook();
        else RemoveHook();
    }

    public void Dispose()
    {
        _releasePoll?.Stop();
        _releasePoll = null;
        RemoveHook();
        if (_source != null)
        {
            if (_registered)
                UnregisterHotKey(_source.Handle, HotkeyId);
            _source.RemoveHook(WndProc);
            _source = null;
        }
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            _spaceDown = true;
            _altDown = true;
            _releasePoll?.Start();
            _onDown();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void PollRelease()
    {
        // While the ring was opened via hotkey, watch for Space or Alt going up.
        bool space = (GetAsyncKeyState(VK_SPACE) & 0x8000) != 0;
        bool alt = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

        if (_spaceDown && !space)
        {
            _spaceDown = false;
            _releasePoll?.Stop();
            _onUp();
            return;
        }

        if (_altDown && !alt && _spaceDown)
        {
            // Alt released while space still notionally held — treat as release.
            _spaceDown = false;
            _altDown = false;
            _releasePoll?.Stop();
            _onUp();
        }

        if (!_spaceDown && !space)
            _releasePoll?.Stop();
    }

    private void InstallHook()
    {
        if (_hook != IntPtr.Zero) return;
        _hookProc = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc,
            GetModuleHandle(IntPtr.Zero), 0);
    }

    private void RemoveHook()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _hookProc = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _doubleTapEnabled)
        {
            var msg = wParam.ToInt32();
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode == VK_MENU)
            {
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    // Only count when Alt is pressed alone (no other modifiers yet).
                    var now = Environment.TickCount64 / 1000.0;
                    if (now - _lastAltPressUptime < 0.35)
                    {
                        _lastAltPressUptime = 0;
                        // Fire on UI thread
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(_onDoubleTapAlt);
                    }
                    else
                    {
                        _lastAltPressUptime = now;
                    }
                }
                else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    // Mirror Mac OptionTapMonitor: Alt up can complete hold-and-release.
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(_onUp);
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private const int WM_HOTKEY = 0x0312;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_SPACE = 0x20;
    private const int VK_MENU = 0x12; // Alt
    private const int WH_KEYBOARD_LL = 13;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private static IntPtr GetModuleHandle(IntPtr _) => GetModuleHandle(null);
}
