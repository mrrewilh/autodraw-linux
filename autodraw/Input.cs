using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Autodraw;

public static class Input
{
    private const string Lib = "libautodraw_uinput.so";

    [DllImport(Lib, ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mouse_init(int width, int height);

    [DllImport(Lib, ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mouse_move_abs(IntPtr handle, int x, int y, ulong interval_100ns);

    [DllImport(Lib, ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mouse_click(IntPtr handle, int button, int is_pressed, ulong click_delay_100ns);

    [DllImport(Lib, ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mouse_destroy(IntPtr handle);

    [DllImport(Lib, ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int key_event(IntPtr handle, ushort key_code, int pressed);

    [DllImport(Lib, ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int paste_hex(IntPtr handle, string hex);

    [DllImport(Lib, ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int start_keyboard_listener(string event_path);

    [DllImport(Lib, ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern void stop_keyboard_listener();

    [DllImport(Lib, ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int get_esc_flag();

    [DllImport(Lib, ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int listener_is_running();

    public const ushort KeyEsc = 1;
    public const ushort KeyLeftCtrl = 29;
    public const ushort KeyLeftShift = 42;
    public const ushort KeyV = 47;

    private static IntPtr _mouseHandle = IntPtr.Zero;
    public static Vector2 mousePos;

    public static class MouseTypes
    {
        public const byte MouseLeft = 0;
        public const byte MouseRight = 1;
    }

    private static void EnsureInit()
    {
        if (_mouseHandle != IntPtr.Zero) return;
        int width = int.TryParse(Config.GetEntry("VirtualWidth"), out var vw) ? vw : 2560;
        int height = int.TryParse(Config.GetEntry("VirtualHeight"), out var vh) ? vh : 1440;
        _mouseHandle = mouse_init(width, height);
        if (_mouseHandle == IntPtr.Zero)
            Console.Error.WriteLine($"[Input] mouse_init({width}x{height}) failed — check /dev/uinput permissions.");
        else
            Console.WriteLine($"[Input] Virtual mouse: {width}x{height}");
    }

    public static void Start() => EnsureInit();

    public static void Stop()
    {
        stop_keyboard_listener();
        if (_mouseHandle != IntPtr.Zero) { mouse_destroy(_mouseHandle); _mouseHandle = IntPtr.Zero; }
    }

    public static void StartListener(string eventPath = "/dev/input/event0") { EnsureInit(); start_keyboard_listener(eventPath); }
    public static void StopListener() => stop_keyboard_listener();
    public static bool CheckEscFlag() => _mouseHandle != IntPtr.Zero && get_esc_flag() == 1;
    public static bool IsListenerRunning => listener_is_running() == 1;

    public static void MoveTo(int x, int y)
    {
        EnsureInit();
        if (_mouseHandle == IntPtr.Zero) return;
        mouse_move_abs(_mouseHandle, x, y, (ulong)Drawing.Interval);
        mousePos = new Vector2(x, y);
        GC.KeepAlive(_mouseHandle);
    }

    public static void MoveBy(int dx, int dy) => MoveTo((int)mousePos.X + dx, (int)mousePos.Y + dy);

    public static void SendClick(byte mouseType)
    {
        EnsureInit();
        if (_mouseHandle == IntPtr.Zero) return;
        mouse_click(_mouseHandle, mouseType, 1, (ulong)Drawing.ClickDelay);
        mouse_click(_mouseHandle, mouseType, 0, (ulong)Drawing.ClickDelay);
        GC.KeepAlive(_mouseHandle);
    }

    public static void SendClickDown(byte mouseType)
    {
        EnsureInit();
        if (_mouseHandle == IntPtr.Zero) return;
        mouse_click(_mouseHandle, mouseType, 1, (ulong)Drawing.ClickDelay);
        GC.KeepAlive(_mouseHandle);
    }

    public static void SendClickUp(byte mouseType)
    {
        EnsureInit();
        if (_mouseHandle == IntPtr.Zero) return;
        mouse_click(_mouseHandle, mouseType, 0, (ulong)Drawing.ClickDelay);
        GC.KeepAlive(_mouseHandle);
    }

    public static void SendKeyDown(KeyCode keyCode)
    {
        EnsureInit();
        if (_mouseHandle == IntPtr.Zero) return;
        key_event(_mouseHandle, KeyCodeToLinux(keyCode), 1);
        GC.KeepAlive(_mouseHandle);
    }

    public static void SendKeyUp(KeyCode keyCode)
    {
        EnsureInit();
        if (_mouseHandle == IntPtr.Zero) return;
        key_event(_mouseHandle, KeyCodeToLinux(keyCode), 0);
        GC.KeepAlive(_mouseHandle);
    }

    public static void SendText(string text)
    {
        EnsureInit();
        if (_mouseHandle == IntPtr.Zero) return;
        paste_hex(_mouseHandle, text);
        GC.KeepAlive(_mouseHandle);
    }

    private static ushort KeyCodeToLinux(KeyCode kc) => kc switch
    {
        KeyCode.VcEscape => 1,
        KeyCode.Vc1 => 2, KeyCode.Vc2 => 3, KeyCode.Vc3 => 4,
        KeyCode.Vc4 => 5, KeyCode.Vc5 => 6, KeyCode.Vc6 => 7,
        KeyCode.Vc7 => 8, KeyCode.Vc8 => 9, KeyCode.Vc9 => 10,
        KeyCode.Vc0 => 11,
        KeyCode.VcMinus => 12, KeyCode.VcEquals => 13,
        KeyCode.VcBackspace => 14, KeyCode.VcTab => 15,
        KeyCode.VcQ => 16, KeyCode.VcW => 17, KeyCode.VcE => 18,
        KeyCode.VcR => 19, KeyCode.VcT => 20, KeyCode.VcY => 21,
        KeyCode.VcU => 23, KeyCode.VcI => 24, KeyCode.VcO => 25,
        KeyCode.VcP => 26,
        KeyCode.VcOpenBracket => 27, KeyCode.VcCloseBracket => 28,
        KeyCode.VcLeftControl => 29, KeyCode.VcEnter => 28,
        KeyCode.VcA => 30, KeyCode.VcS => 31, KeyCode.VcD => 32,
        KeyCode.VcF => 33, KeyCode.VcG => 34, KeyCode.VcH => 35,
        KeyCode.VcJ => 36, KeyCode.VcK => 37, KeyCode.VcL => 38,
        KeyCode.VcSemiColon => 39,
        KeyCode.VcQuote => 40, KeyCode.VcBackQuote => 41,
        KeyCode.VcLeftShift => 42, KeyCode.VcBackslash => 43,
        KeyCode.VcZ => 44, KeyCode.VcX => 45, KeyCode.VcC => 46,
        KeyCode.VcV => 47, KeyCode.VcB => 48, KeyCode.VcN => 49,
        KeyCode.VcM => 50,
        KeyCode.VcComma => 51, KeyCode.VcPeriod => 52,
        KeyCode.VcSlash => 53, KeyCode.VcRightShift => 54,
        KeyCode.VcLeftAlt => 56, KeyCode.VcSpace => 57,
        KeyCode.VcF1 => 59, KeyCode.VcF2 => 60, KeyCode.VcF3 => 61,
        KeyCode.VcF4 => 62, KeyCode.VcF5 => 63, KeyCode.VcF6 => 64,
        KeyCode.VcF7 => 65, KeyCode.VcF8 => 66, KeyCode.VcF9 => 67,
        KeyCode.VcF10 => 68, KeyCode.VcF11 => 87, KeyCode.VcF12 => 88,
        _ => 0,
    };
}
