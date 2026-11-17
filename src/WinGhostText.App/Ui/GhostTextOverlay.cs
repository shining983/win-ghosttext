using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using WinGhostText.App.Native;
using WinGhostText.Core;
using MediaColor = System.Windows.Media.Color;
using MediaFontFamily = System.Windows.Media.FontFamily;
using DrawingPoint = System.Drawing.Point;

namespace WinGhostText.App.Ui;

/// <summary>
/// Transparent, click-through, no-activate IME-like suggestion bubble near the caret.
/// Shows the full suggested / corrected word — not an in-line ghost suffix.
/// </summary>
internal sealed class GhostTextOverlay
{
    private const double GapDip = 4.0;
    private const double DefaultFontSizeDip = 12.0 * 96.0 / 72.0;
    private const double MinFontSizeDip = 10.0;
    private const double MaxFontSizeDip = 16.0;
    private static readonly SolidColorBrush LightPanelBrush = CreateFrozenBrush(0xF2, 0xF7, 0xF7, 0xF7);
    private static readonly SolidColorBrush DarkPanelBrush = CreateFrozenBrush(0xF2, 0x2D, 0x2D, 0x2D);
    private static readonly SolidColorBrush LightTextBrush = CreateFrozenBrush(0xFF, 0x1A, 0x1A, 0x1A);
    private static readonly SolidColorBrush DarkTextBrush = CreateFrozenBrush(0xFF, 0xF0, 0xF0, 0xF0);
    private static readonly SolidColorBrush LightBorderBrush = CreateFrozenBrush(0xCC, 0xC0, 0xC0, 0xC0);
    private static readonly SolidColorBrush DarkBorderBrush = CreateFrozenBrush(0xCC, 0x55, 0x55, 0x55);

    private static readonly SolidColorBrush UnderlineBrush = CreateFrozenBrush(0xE6, 0xD9, 0x4F, 0x3C);

    private readonly Window _window;
    private readonly Border _panel;
    private readonly TextBlock _text;
    private readonly Window _underlineWindow;
    private readonly Border _underlineBar;
    private bool _hooked;
    private bool _underlineHooked;

    public GhostTextOverlay()
    {
        _text = new TextBlock
        {
            Foreground = LightTextBrush,
            Background = System.Windows.Media.Brushes.Transparent,
            FontSize = DefaultFontSizeDip,
            FontFamily = new MediaFontFamily("Segoe UI"),
            FontWeight = FontWeights.Normal,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            IsHitTestVisible = false
        };

        _panel = new Border
        {
            Background = LightPanelBrush,
            BorderBrush = LightBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Child = _text,
            IsHitTestVisible = false
        };

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            Focusable = false,
            IsHitTestVisible = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = _panel
        };

        _underlineBar = new Border
        {
            Background = UnderlineBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(1),
            Height = 2,
            Width = 24,
            IsHitTestVisible = false
        };

        _underlineWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            Focusable = false,
            IsHitTestVisible = false,
            SizeToContent = SizeToContent.Manual,
            Width = 24,
            Height = 2,
            Content = _underlineBar
        };

        _window.SourceInitialized += (_, _) => ApplyClickThrough();
        _underlineWindow.SourceInitialized += (_, _) => ApplyUnderlineClickThrough();
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

            if (_underlineWindow.IsVisible)
            {
                _underlineWindow.Hide();
            }

            _text.Text = string.Empty;
        }
        else
        {
            _window.Dispatcher.Invoke(Hide);
        }
    }

    /// <summary>
    /// Show the primary suggestion bubble anchored near the caret.
    /// Prefers below the caret; flips above when the work area has insufficient space.
    /// Optional thin underline marks the bad span for word-end corrections.
    /// </summary>
    public void Show(
        CompletionSuggestion suggestion,
        DrawingPoint caretScreen,
        int caretHeightPx,
        uint targetDpi = 0,
        float? preferredFontSizeDip = null,
        bool showUnderline = false)
    {
        void Apply()
        {
            // Full suggested / corrected word (single primary candidate).
            // FontProbe may be weak on Win11 Notepad — defaults keep the bubble visible.
            _text.Text = suggestion.DisplayWord;
            ApplyTheme(caretScreen);
            _text.FontSize = ResolveBubbleFontSize(preferredFontSizeDip);

            // Create HWND first so per-monitor DPI is correct before measuring.
            ApplyClickThrough();

            var (scaleX, scaleY) = ResolveDpiScales(targetDpi);
            // Guard against empty caret: still show near work-area origin rather than skipping.
            var caretX = caretScreen.X;
            var caretY = caretScreen.Y;
            if (caretX <= 2 && caretY <= 2)
            {
                var wa = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
                         ?? new System.Drawing.Rectangle(0, 0, 800, 600);
                caretX = wa.Left + 48;
                caretY = wa.Top + 48;
            }

            var dipX = caretX / scaleX;
            var caretTopDip = caretY / scaleY;
            var caretHeightDip = caretHeightPx > 0 ? caretHeightPx / scaleY : DefaultFontSizeDip * 1.2;
            var caretBottomDip = caretTopDip + caretHeightDip;

            _panel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            _panel.Arrange(new System.Windows.Rect(_panel.DesiredSize));
            _window.UpdateLayout();
            if (!_window.IsMeasureValid || !_window.IsArrangeValid)
            {
                _window.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                _window.Arrange(new System.Windows.Rect(_window.DesiredSize));
            }

            var bubbleWidth = Math.Max(_window.ActualWidth, Math.Max(_window.DesiredSize.Width, _panel.DesiredSize.Width));
            var bubbleHeight = Math.Max(_window.ActualHeight, Math.Max(_window.DesiredSize.Height, _panel.DesiredSize.Height));
            if (bubbleWidth <= 0 || bubbleHeight <= 0)
            {
                bubbleWidth = Math.Max(bubbleWidth, 80);
                bubbleHeight = Math.Max(bubbleHeight, 28);
            }

            var work = GetWorkAreaDip(new DrawingPoint(caretX, caretY), scaleX, scaleY);
            var left = dipX;
            // Prefer below caret; flip above if not enough room.
            var topBelow = caretBottomDip + GapDip;
            var topAbove = caretTopDip - GapDip - bubbleHeight;
            double top;
            if (topBelow + bubbleHeight <= work.Bottom - 2)
            {
                top = topBelow;
            }
            else if (topAbove >= work.Top + 2)
            {
                top = topAbove;
            }
            else
            {
                // Clamp into work area when neither side fits cleanly.
                top = Math.Clamp(topBelow, work.Top + 2, Math.Max(work.Top + 2, work.Bottom - bubbleHeight - 2));
            }

            // Keep the bubble on-screen horizontally.
            if (left + bubbleWidth > work.Right - 2)
            {
                left = Math.Max(work.Left + 2, work.Right - bubbleWidth - 2);
            }

            if (left < work.Left + 2)
            {
                left = work.Left + 2;
            }

            _window.Left = left;
            _window.Top = top;

            if (!_window.IsVisible)
            {
                _window.Show();
            }

            ApplyClickThrough();

            if (showUnderline && suggestion.IsCorrectionMark)
            {
                ShowUnderline(suggestion, new DrawingPoint(caretX, caretY), caretHeightPx, scaleX, scaleY);
            }
            else if (_underlineWindow.IsVisible)
            {
                _underlineWindow.Hide();
            }
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

    private void ShowUnderline(
        CompletionSuggestion suggestion,
        DrawingPoint caretScreen,
        int caretHeightPx,
        double scaleX,
        double scaleY)
    {
        // Approximate the bad span just before the caret (exclude trailing delimiter).
        var mark = suggestion.DisplayWord;
        if (string.IsNullOrEmpty(mark))
        {
            if (_underlineWindow.IsVisible)
            {
                _underlineWindow.Hide();
            }

            return;
        }

        ApplyUnderlineClickThrough();
        var fontSize = ResolveBubbleFontSize(null);
        // Rough average glyph width — enough for a short marker, not pixel-perfect.
        var approxCharDip = fontSize * 0.55;
        var widthDip = Math.Clamp(mark.Length * approxCharDip, 12.0, 160.0);
        var caretHeightDip = caretHeightPx > 0 ? caretHeightPx / scaleY : fontSize * 1.2;
        var left = (caretScreen.X / scaleX) - widthDip;
        var top = (caretScreen.Y / scaleY) + caretHeightDip - 1.0;

        _underlineWindow.Width = widthDip;
        _underlineWindow.Height = 2;
        _underlineBar.Width = widthDip;
        _underlineWindow.Left = left;
        _underlineWindow.Top = top;

        if (!_underlineWindow.IsVisible)
        {
            _underlineWindow.Show();
        }

        ApplyUnderlineClickThrough();
    }

    private void ApplyUnderlineClickThrough()
    {
        var helper = new WindowInteropHelper(_underlineWindow);
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
        _underlineHooked = true;
    }

    private static double ResolveBubbleFontSize(float? preferredFontSizeDip)
    {
        if (preferredFontSizeDip is > 0)
        {
            // Lightly scale toward the host size, but keep the bubble readable and compact.
            return Math.Clamp(preferredFontSizeDip.Value, MinFontSizeDip, MaxFontSizeDip);
        }

        return DefaultFontSizeDip;
    }

    private (double ScaleX, double ScaleY) ResolveDpiScales(uint targetDpi)
    {
        if (targetDpi > 0)
        {
            var scale = targetDpi / (double)NativeMethods.DEFAULT_DPI;
            if (scale > 0)
            {
                return (scale, scale);
            }
        }

        var dpi = VisualTreeHelper.GetDpi(_window);
        var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
        return (scaleX, scaleY);
    }

    private static Rect GetWorkAreaDip(DrawingPoint caretScreen, double scaleX, double scaleY)
    {
        try
        {
            var screen = System.Windows.Forms.Screen.FromPoint(caretScreen);
            var wa = screen.WorkingArea;
            return new Rect(
                wa.Left / scaleX,
                wa.Top / scaleY,
                wa.Width / scaleX,
                wa.Height / scaleY);
        }
        catch
        {
            return new Rect(0, 0, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
        }
    }

    private void ApplyTheme(DrawingPoint caretScreen)
    {
        var isLight = ResolveIsLightBackground(caretScreen);
        if (isLight)
        {
            _panel.Background = LightPanelBrush;
            _panel.BorderBrush = LightBorderBrush;
            _text.Foreground = LightTextBrush;
        }
        else
        {
            _panel.Background = DarkPanelBrush;
            _panel.BorderBrush = DarkBorderBrush;
            _text.Foreground = DarkTextBrush;
        }
    }

    private static bool ResolveIsLightBackground(DrawingPoint caretScreen)
    {
        if (TrySampleBackgroundLuma(caretScreen, out var bgLuma))
        {
            return bgLuma >= 128;
        }

        return AppsUseLightTheme();
    }

    private static bool TrySampleBackgroundLuma(DrawingPoint caretScreen, out int luma)
    {
        luma = 128;
        var hdc = NativeMethods.GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            // Sample empty editor chrome near the caret (not the caret bar itself).
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
                if (pixel == 0xFFFFFFFF)
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

    private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(MediaColor.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
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
