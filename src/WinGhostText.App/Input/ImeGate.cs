using WinGhostText.App.Native;

namespace WinGhostText.App.Input;

/// <summary>
/// Suppresses GhostText while Chinese (or other CJK) IME is in native/composition mode.
/// English mode (e.g. WeChat IME after Shift) must allow suggestions immediately.
/// </summary>
internal static class ImeGate
{
    private const int LangChinese = 0x04;
    private const int LangJapanese = 0x11;
    private const int LangKorean = 0x12;

    /// <summary>
    /// True when completions and Tab intercept must be disabled for this HWND.
    /// </summary>
    public static bool ShouldSuppressCompletions(IntPtr hwnd) =>
        IsCompositionActive(hwnd) || IsNativeImeMode(hwnd);

    public static bool IsCompositionActive(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (!TryGetImc(hwnd, out var ctxHwnd, out var himc))
        {
            return false;
        }

        try
        {
            var len = NativeMethods.ImmGetCompositionStringW(
                himc, NativeMethods.GCS_COMPSTR, null, 0);
            return len > 0;
        }
        finally
        {
            NativeMethods.ImmReleaseContext(ctxHwnd, himc);
        }
    }

    /// <summary>
    /// True when the focus thread's IME is in native (e.g. Chinese) conversion mode.
    /// Shift-to-English clears <see cref="NativeMethods.IME_CMODE_NATIVE"/> and returns false.
    /// </summary>
    public static bool IsNativeImeMode(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        var hkl = NativeMethods.GetKeyboardLayout(threadId);
        var langId = (int)(hkl.ToInt64() & 0xFFFF);
        var primaryLang = langId & 0x3FF;
        var isCjkLayout = primaryLang is LangChinese or LangJapanese or LangKorean;

        if (!TryGetImc(hwnd, out var ctxHwnd, out var himc))
        {
            // No IMC: cannot read conversion bits; only composition (handled elsewhere) gates.
            return false;
        }

        try
        {
            if (!NativeMethods.ImmGetConversionStatus(himc, out var conversion, out _))
            {
                return false;
            }

            var native = (conversion & NativeMethods.IME_CMODE_NATIVE) != 0;
            if (!native)
            {
                // English / alphanumeric mode — allow GhostText even on a Chinese layout.
                return false;
            }

            // Native conversion on a CJK layout (WeChat / MS Pinyin Chinese mode).
            if (isCjkLayout)
            {
                return true;
            }

            // Some IMEs keep a non-CJK HKL while native is set and open.
            return NativeMethods.ImmGetOpenStatus(himc);
        }
        finally
        {
            NativeMethods.ImmReleaseContext(ctxHwnd, himc);
        }
    }

    private static bool TryGetImc(IntPtr hwnd, out IntPtr ctxHwnd, out IntPtr himc)
    {
        ctxHwnd = hwnd;
        himc = NativeMethods.ImmGetContext(hwnd);
        if (himc != IntPtr.Zero)
        {
            return true;
        }

        // Also try the root owner / ancestor — some hosts attach IMC there.
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (root != IntPtr.Zero && root != hwnd)
        {
            himc = NativeMethods.ImmGetContext(root);
            if (himc != IntPtr.Zero)
            {
                ctxHwnd = root;
                return true;
            }
        }

        return false;
    }
}
