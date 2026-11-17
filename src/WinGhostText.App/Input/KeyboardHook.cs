using System.Diagnostics;
using System.Runtime.InteropServices;
using WinGhostText.App.Native;

namespace WinGhostText.App.Input;

internal sealed class KeyboardHook : IDisposable
{
    public event Action<KeyEventArgs>? KeyDown;

    private NativeMethods.LowLevelKeyboardProc? _proc;
    private IntPtr _hook = IntPtr.Zero;
    private bool _disposed;

    public void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);

        if (_hook == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Failed to install keyboard hook. Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 &&
            (wParam == (IntPtr)NativeMethods.WM_KEYDOWN ||
             wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var args = new KeyEventArgs((int)info.vkCode);
            try
            {
                KeyDown?.Invoke(args);
            }
            catch
            {
                // Never break the hook chain on handler failures.
            }

            if (args.Handled)
            {
                return (IntPtr)1;
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    internal sealed class KeyEventArgs(int virtualKey)
    {
        public int VirtualKey { get; } = virtualKey;
        public bool Handled { get; set; }
    }
}

internal static class VirtualKeys
{
    public const int Tab = 0x09;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int Back = 0x08;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int Delete = 0x2E;
    public const int Home = 0x24;
    public const int End = 0x23;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12; // Alt
}
