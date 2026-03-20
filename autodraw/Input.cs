using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Autodraw;

public static class Input
{
    // ── Native library (libautodraw_uinput.so) ────────────────────────────

    private const string Lib = "libautodraw_uinput.so";

    [DllImport(Lib, ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mouse_init(int width, int height);

    [DllImport(Lib, ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mouse_move_abs(IntPtr handle, int x, int y, ulong interval_100ns);

    [DllImport(Lib, ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mouse_click(IntPtr handle, int button, int is_pressed, ulong click_delay_100ns);

    [DllImport(Lib, ExactSpelling = true, SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern void mouse_destroy(IntPtr handle);

    // ── State ────────────────────────────────────────────────────────────

    private static IntPtr _mouseHandle = IntPtr.Zero;

    public static Vector2 mousePos;

    public static class MouseTypes
    {
        public const byte MouseLeft = 0;
        public const byte MouseRight = 1;
    }

    // ── Core ─────────────────────────────────────────────────────────────

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
        if (_mouseHandle != IntPtr.Zero)
        {
            mouse_destroy(_mouseHandle);
            _mouseHandle = IntPtr.Zero;
        }
    }

    // ── Movement (absolute coordinates, interval in 100ns ticks) ─────────

    public static void MoveTo(int x, int y)
    {
        EnsureInit();
        if (_mouseHandle == IntPtr.Zero) return;

        mouse_move_abs(_mouseHandle, x, y, (ulong)Drawing.Interval);

        mousePos = new Vector2(x, y);

        GC.KeepAlive(_mouseHandle);
    }

    public static void MoveBy(int dx, int dy)
    {
        MoveTo((int)mousePos.X + dx, (int)mousePos.Y + dy);
    }

    // ── Click Handling ───────────────────────────────────────────────────

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

    // ── Keyboard simulation (TODO: uinput keyboard) ──────────────────────

    public static void SendKeyDown(KeyCode keyCode) { }
    public static void SendKeyUp(KeyCode keyCode) { }
    public static void SendText(string text) { }
}
