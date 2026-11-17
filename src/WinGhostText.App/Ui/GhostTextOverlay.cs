using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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
    private readonly Window _window;
    private readonly TextBlock _text;
    private bool _hooked;

    public GhostTextOverlay()
    {
        _text = new TextBlock
        {
            Foreground = new SolidColorBrush(MediaColor.FromArgb(0x99, 0x80, 0x80, 0x80)),
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
        int targetCellHeightPx = 0)
    {
        void Apply()
        {
            _text.Text = suggestion.GhostDisplay;
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

            var dpi = VisualTreeHelper.GetDpi(_window);
            var pixelsPerDip = dpi.PixelsPerDip > 0 ? dpi.PixelsPerDip : 1.0;
            var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

            // Physical caret → DIP using the overlay's monitor scale (PerMonitorV2).
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
                top = dipY - 1;
            }

            _window.Left = dipX + 1;
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
