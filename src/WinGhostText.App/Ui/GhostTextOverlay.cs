using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using WinGhostText.App.Native;
using WinGhostText.Core;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace WinGhostText.App.Ui;

/// <summary>
/// Transparent, click-through, no-activate overlay showing the gray GhostText suffix.
/// </summary>
internal sealed class GhostTextOverlay
{
    // Light theme: darker mid-gray so the suffix reads as ghost on pale editors.
    private static readonly SolidColorBrush LightThemeGhostBrush = CreateFrozenBrush(0xD0, 0x5A, 0x5A, 0x5A);
    // Dark theme (Win11 dark Notepad): brighter #B0B0B0 with higher alpha for contrast.
    private static readonly SolidColorBrush DarkThemeGhostBrush = CreateFrozenBrush(0xE6, 0xB0, 0xB0, 0xB0);

    /// <summary>Minimum |blendedFg - bgLuma| before we swap to the other theme brush.</summary>
    private const int MinContrastDelta = 48;

    private readonly Window _window;
    private readonly TextBlock _text;
    private bool _hooked;

    public GhostTextOverlay()
    {
        _text = new TextBlock
        {
            Foreground = LightThemeGhostBrush,
            Background = MediaBrushes.Transparent,
            // 11pt in DIPs — replaced by live probe whenever available.
            FontSize = 11.0 * 96.0 / 72.0,
            FontFamily = new MediaFontFamily("Segoe UI"),
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            IsHitTestVisible = false
        };

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = MediaBrushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            Focusable = false,
            IsHitTestVisible = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = new Border
            {
                Background = MediaBrushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Child = _text
            }
        };

        _window.SourceInitialized += (_, _) => ApplyClickThrough();
    }

    public bool IsVisible => _window.IsVisible;

    public void Hide()
    {
        if (_window.Dispatcher.CheckAccess())
        {
            if (_window.IsVisible)
            {
                _window.Hide();
            }

            _text.Text = string.Empty;
        }
        else
        {
            _window.Dispatcher.Invoke(Hide);
        }
    }

    public void Show(
        CompletionSuggestion suggestion,
        System.Drawing.Point caretScreen,
        string? fontFamily,
        float? fontSizeDip,
        int? fontWeight = null,
        int targetCellHeightPx = 0,
        uint targetDpi = 0)
    {
        void Apply()
        {
            _text.Text = suggestion.GhostDisplay;
            _text.Foreground = ResolveGhostBrush(caretScreen);
            if (!string.IsNullOrWhiteSpace(fontFamily))
            {
                // Prefer the probed face; allow WPF fallbacks if the face is missing locally.
                _text.FontFamily = new MediaFontFamily(
                    fontFamily + ", Segoe UI Variable, Cascadia Mono, Consolas, Segoe UI");
            }
            else
            {
                _text.FontFamily = new MediaFontFamily(
                    "Segoe UI Variable, Cascadia Mono, Consolas, Segoe UI");
            }

            if (fontSizeDip is > 0)
            {
                // Probe already converted points / GDI pixels into WPF DIPs.
                _text.FontSize = fontSizeDip.Value;
            }

            if (fontWeight is > 0 and <= 1000)
            {
                _text.FontWeight = FontWeight.FromOpenTypeWeight(fontWeight.Value);
            }
            else
            {
                _text.FontWeight = FontWeights.Normal;
            }

            // Create HWND first (no Show yet) so per-monitor DPI / FormattedText are correct.
            ApplyClickThrough();

            // Prefer the focused editor's GetDpiForWindow so physical px ↔ DIP matches the caret.
            var (scaleX, scaleY, pixelsPerDip) = ResolveDpiScales(targetDpi);

            // Physical caret → DIP using the editor (or overlay) monitor scale (PerMonitorV2).
            var dipX = caretScreen.X / scaleX;
            var dipY = caretScreen.Y / scaleY;
            var cellHeightDip = targetCellHeightPx > 0
                ? targetCellHeightPx / scaleY
                : 0;

            // Calibrate em size so the laid-out line box matches the host caret/glyph cell.
            // Caret/line height is not FontSize; without this, GhostText stays visually small.
            if (cellHeightDip > 0 && !string.IsNullOrEmpty(_text.Text))
            {
                CalibrateToCellHeight(cellHeightDip, pixelsPerDip);
            }

            _text.LineHeight = _text.FontSize;

            var metrics = MeasureText(pixelsPerDip);
            double top;
            if (cellHeightDip > 0 && metrics.Height > 0 && metrics.Baseline > 0)
            {
                // Match overlay baseline to host cell baseline (proportional within the caret box).
                var hostBaseline = dipY + cellHeightDip * (metrics.Baseline / metrics.Height);
                top = hostBaseline - metrics.Baseline;
            }
            else if (cellHeightDip > 0 && metrics.Height > 0)
            {
                top = dipY + (cellHeightDip - metrics.Height) / 2.0;
            }
            else
            {
                // No caret-cell metrics: align top to caret top (no brittle caretTop-1 fudge).
                top = dipY;
            }

            // Caret screen point is already the insertion Right edge — no extra horizontal nudge.
            _window.Left = dipX;
            _window.Top = top;

            if (!_window.IsVisible)
            {
                _window.Show();
            }

            ApplyClickThrough();
        }

        if (_window.Dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            _window.Dispatcher.Invoke(Apply);
        }
    }

    private (double ScaleX, double ScaleY, double PixelsPerDip) ResolveDpiScales(uint targetDpi)
    {
        if (targetDpi > 0)
        {
            var scale = targetDpi / (double)NativeMethods.DEFAULT_DPI;
            if (scale > 0)
            {
                return (scale, scale, scale);
            }
        }

        var dpi = VisualTreeHelper.GetDpi(_window);
        var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
        var pixelsPerDip = dpi.PixelsPerDip > 0 ? dpi.PixelsPerDip : 1.0;
        return (scaleX, scaleY, pixelsPerDip);
    }

    private void CalibrateToCellHeight(double cellHeightDip, double pixelsPerDip)
    {
        // Iterate a couple of times — typeface metrics are mildly non-linear with size.
        for (var i = 0; i < 3; i++)
        {
            var measured = MeasureText(pixelsPerDip);
            if (measured.Height <= 0.1)
            {
                return;
            }

            var scale = cellHeightDip / measured.Height;
            if (Math.Abs(scale - 1.0) < 0.02)
            {
                return;
            }

            // Keep FontSize in a sane range (6–96 DIP).
            var next = _text.FontSize * scale;
            _text.FontSize = Math.Clamp(next, 6.0, 96.0);
            _text.LineHeight = _text.FontSize;
        }
    }

    private FormattedText MeasureText(double pixelsPerDip)
    {
        return new FormattedText(
            _text.Text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(_text.FontFamily, _text.FontStyle, _text.FontWeight, _text.FontStretch),
            _text.FontSize,
            _text.Foreground,
            pixelsPerDip);
    }

    private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(MediaColor.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Pick a darker (light theme) or lighter (dark theme) mid-gray so the suffix stays visible
    /// without matching typed text. Prefers sampling the editor background near the caret;
    /// swaps brushes when contrast against the sampled background is too low.
    /// </summary>
    private static SolidColorBrush ResolveGhostBrush(System.Drawing.Point caretScreen)
    {
        if (TrySampleBackgroundLuma(caretScreen, out var bgLuma))
        {
            var isLight = bgLuma >= 128;
            var primary = isLight ? LightThemeGhostBrush : DarkThemeGhostBrush;
            if (ContrastDelta(primary, bgLuma) >= MinContrastDelta)
            {
                return primary;
            }

            var fallback = isLight ? DarkThemeGhostBrush : LightThemeGhostBrush;
            if (ContrastDelta(fallback, bgLuma) > ContrastDelta(primary, bgLuma))
            {
                return fallback;
            }

            return primary;
        }

        return AppsUseLightTheme() ? LightThemeGhostBrush : DarkThemeGhostBrush;
    }

    /// <summary>
    /// Approximate luminance delta between a semi-transparent ghost brush and an opaque background.
    /// </summary>
    internal static int ContrastDelta(SolidColorBrush brush, int bgLuma)
    {
        var c = brush.Color;
        var fgLuma = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
        var alpha = c.A / 255.0;
        var blended = (int)Math.Round(fgLuma * alpha + bgLuma * (1.0 - alpha));
        return Math.Abs(blended - bgLuma);
    }

    private static bool TrySampleBackgroundLuma(System.Drawing.Point caretScreen, out int luma)
    {
        luma = 128;
        var hdc = NativeMethods.GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            // Prefer points that are usually empty editor chrome (above / ahead of glyphs),
            // not the caret bar or typed text — especially important on dark Notepad.
            var samples = new (int X, int Y)[]
            {
                (caretScreen.X + 28, Math.Max(0, caretScreen.Y - 4)),
                (caretScreen.X + 40, caretScreen.Y + 2),
                (caretScreen.X + 16, Math.Max(0, caretScreen.Y - 6)),
                (Math.Max(0, caretScreen.X - 2), Math.Max(0, caretScreen.Y - 8))
            };

            long lumaSum = 0;
            var count = 0;
            foreach (var (x, y) in samples)
            {
                var pixel = NativeMethods.GetPixel(hdc, x, y);
                if (pixel == 0xFFFFFFFF) // GetPixel failure sentinel
                {
                    continue;
                }

                var r = (int)(pixel & 0xFF);
                var g = (int)((pixel >> 8) & 0xFF);
                var b = (int)((pixel >> 16) & 0xFF);
                lumaSum += (r * 299 + g * 587 + b * 114) / 1000;
                count++;
            }

            if (count == 0)
            {
                return false;
            }

            luma = (int)(lumaSum / count);
            return true;
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    private static bool AppsUseLightTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is int i ? i != 0 : true;
        }
        catch
        {
            return true;
        }
    }

    private void ApplyClickThrough()
    {
        var helper = new WindowInteropHelper(_window);
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero)
        {
            helper.EnsureHandle();
            hwnd = helper.Handle;
        }

        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TRANSPARENT |
              NativeMethods.WS_EX_TOOLWINDOW |
              NativeMethods.WS_EX_NOACTIVATE |
              NativeMethods.WS_EX_LAYERED;
        _ = NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));

        if (!_hooked)
        {
            _hooked = true;
        }
    }
}
