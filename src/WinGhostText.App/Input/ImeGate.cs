using WinGhostText.App.Native;

namespace WinGhostText.App.Input;

/// <summary>
/// Blocks GhostText while Chinese (or other) IME composition is active.
/// </summary>
internal static class ImeGate
{
    public static bool IsCompositionActive(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var himc = NativeMethods.ImmGetContext(hwnd);
        if (himc == IntPtr.Zero)
        {
            // Also try the root owner / ancestor — some hosts attach IMC there.
            var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root != IntPtr.Zero && root != hwnd)
            {
                himc = NativeMethods.ImmGetContext(root);
                hwnd = root;
            }
        }

        if (himc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var len = NativeMethods.ImmGetCompositionStringW(
                himc, NativeMethods.GCS_COMPSTR, null, 0);
            if (len > 0)
            {
                return true;
            }

            // Open IME with non-Latin keyboard layouts: still allow English GhostText
            // only when there is no composition string. Composition is the hard gate.
            return false;
        }
        finally
        {
            NativeMethods.ImmReleaseContext(hwnd, himc);
        }
    }
}
