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
                Child = _text,
                Padding = new Thickness(0)
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
        int? fontWeight = null)
    {
        void Apply()
        {
            _text.Text = suggestion.GhostDisplay;
            if (!string.IsNullOrWhiteSpace(fontFamily))
            {
                // Prefer the probed face; allow WPF fallbacks if the face is missing locally.
                _text.FontFamily = new MediaFontFamily(fontFamily + ", Cascadia Mono, Consolas, Segoe UI");
            }
            else
            {
                _text.FontFamily = new MediaFontFamily("Cascadia Mono, Consolas, Segoe UI");
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

            // Offset slightly to the right of the caret so we don't cover the typed prefix.
            // Caret coordinates are in physical pixels; convert for WPF when needed.
            var source = _window.IsVisible
                ? PresentationSource.FromVisual(_window)
                : null;

            double dipX = caretScreen.X;
            double dipY = caretScreen.Y;
            if (source?.CompositionTarget is not null)
            {
                var transform = source.CompositionTarget.TransformFromDevice;
                var dip = transform.Transform(new System.Windows.Point(caretScreen.X, caretScreen.Y));
                dipX = dip.X;
                dipY = dip.Y;
            }
            else
            {
                // Approximate using system DPI before the window is shown.
                using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
                dipX = caretScreen.X * 96.0 / g.DpiX;
                dipY = caretScreen.Y * 96.0 / g.DpiY;
            }

            _window.Left = dipX + 1;
            _window.Top = dipY - 1;

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
