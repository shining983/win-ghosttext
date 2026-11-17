using System.Windows.Threading;
using WinGhostText.App.Input;
using WinGhostText.App.Ui;
using WinGhostText.Core;

namespace WinGhostText.App.Services;

/// <summary>
/// Orchestrates hooks, gates, trie suggestions, word-end marks, suggestion bubble, and accept/dismiss.
/// Mid-word: completion wins. After word-end (space/punct): correction marks win.
/// Architecture is global-app ready; V1 acceptance target is Notepad.
/// </summary>
internal sealed class CompletionController : IDisposable
{
    private readonly CompletionEngine _engine;
    private readonly GhostTextOverlay _overlay = new();
    private readonly KeyboardHook _hook = new();
    private readonly object _gate = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _wordEndTimer;

    private CompletionSuggestion? _current;
    private IntPtr _lastHwnd;
    private bool _enabled = true;
    private bool _suppressHook;
    private bool _disposed;
    private bool _pendingWordEnd;

    public CompletionController(CompletionEngine engine, Dispatcher? dispatcher = null)
    {
        _engine = engine;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(15)
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            RefreshSuggestion(wordEnd: false);
        };

        // Slightly longer so the host commits the space/punct before we read text.
        _wordEndTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        _wordEndTimer.Tick += (_, _) =>
        {
            _wordEndTimer.Stop();
            RefreshSuggestion(wordEnd: true);
        };

        _hook.KeyDown += OnKeyDown;
    }

    public bool Enabled
    {
        get { lock (_gate) return _enabled; }
        set
        {
            lock (_gate)
            {
                _enabled = value;
                if (!value)
                {
                    Dismiss();
                }
            }
        }
    }

    public void Start() => _hook.Start();

    public void Stop()
    {
        _refreshTimer.Stop();
        _wordEndTimer.Stop();
        Dismiss();
        _hook.Stop();
    }

    private void OnKeyDown(KeyboardHook.KeyEventArgs e)
    {
        if (_suppressHook)
        {
            return;
        }

        if (!Enabled)
        {
            return;
        }

        if (IsDown(VirtualKeys.Control) || IsDown(VirtualKeys.Menu))
        {
            Dismiss();
            return;
        }

        switch (e.VirtualKey)
        {
            case VirtualKeys.Escape:
                if (_current is not null)
                {
                    e.Handled = true;
                    Dismiss();
                }

                return;

            case VirtualKeys.Tab:
                if (_current is not null)
                {
                    // Never intercept Tab while Chinese/native IME is active.
                    if (IsImeSuppressing())
                    {
                        Dismiss();
                        return;
                    }

                    e.Handled = true;
                    Accept();
                }

                return;

            case VirtualKeys.Space:
                // Space never accepts — insert a normal space, then run word-end marks.
                DismissCompletionOnly();
                ScheduleWordEnd();
                return;

            case VirtualKeys.Left:
            case VirtualKeys.Right:
            case VirtualKeys.Up:
            case VirtualKeys.Down:
            case VirtualKeys.Home:
            case VirtualKeys.End:
            case VirtualKeys.Delete:
                Dismiss();
                return;

            case VirtualKeys.Back:
                ScheduleRefresh();
                return;
        }

        if (e.VirtualKey is >= 0x41 and <= 0x5A)
        {
            // Mid-word typing: completion wins over any lingering mark.
            ScheduleRefresh();
            return;
        }

        // Digits / OEM punctuation: treat as word-end when they typically finish a token.
        if (e.VirtualKey is (>= 0x30 and <= 0x39) || e.VirtualKey is (>= 0xBA and <= 0xDF))
        {
            if (IsLikelyWordEndPunctuation(e.VirtualKey))
            {
                DismissCompletionOnly();
                ScheduleWordEnd();
            }
            else
            {
                Dismiss();
            }
        }
    }

    private static bool IsLikelyWordEndPunctuation(int vk) =>
        vk is 0xBE // VK_OEM_PERIOD .
            or 0xBC // VK_OEM_COMMA ,
            or 0xBF // VK_OEM_2 /
            or 0xBA // VK_OEM_1 ;
            or 0xDE // VK_OEM_7 '
            or 0xBB // VK_OEM_PLUS =
            or 0xBD; // VK_OEM_MINUS -

    private void Accept()
    {
        CompletionSuggestion? suggestion;
        lock (_gate)
        {
            suggestion = _current;
            _current = null;
            _pendingWordEnd = false;
        }

        _overlay.Hide();
        if (suggestion is null)
        {
            return;
        }

        try
        {
            _suppressHook = true;
            TextInjector.Accept(suggestion);
        }
        finally
        {
            _suppressHook = false;
        }
    }

    private void Dismiss()
    {
        lock (_gate)
        {
            _current = null;
            _pendingWordEnd = false;
        }

        _refreshTimer.Stop();
        _wordEndTimer.Stop();
        _overlay.Hide();
    }

    /// <summary>
    /// Clear the mid-word completion bubble without canceling a scheduled word-end check.
    /// </summary>
    private void DismissCompletionOnly()
    {
        lock (_gate)
        {
            if (_current is { IsCorrectionMark: false })
            {
                _current = null;
            }
            else if (_current is { IsCorrectionMark: true })
            {
                // Space/punct while a mark is showing: dismiss mark (Space does not accept).
                _current = null;
            }
        }

        _overlay.Hide();
    }

    private void ScheduleRefresh()
    {
        lock (_gate)
        {
            _pendingWordEnd = false;
        }

        _wordEndTimer.Stop();

        void Restart()
        {
            _refreshTimer.Stop();
            _refreshTimer.Start();
        }

        if (_dispatcher.CheckAccess())
        {
            Restart();
        }
        else
        {
            _ = _dispatcher.BeginInvoke(Restart);
        }
    }

    private void ScheduleWordEnd()
    {
        lock (_gate)
        {
            _pendingWordEnd = true;
        }

        _refreshTimer.Stop();

        void Restart()
        {
            _wordEndTimer.Stop();
            _wordEndTimer.Start();
        }

        if (_dispatcher.CheckAccess())
        {
            Restart();
        }
        else
        {
            _ = _dispatcher.BeginInvoke(Restart);
        }
    }

    private void RefreshSuggestion(bool wordEnd)
    {
        try
        {
            var focus = FocusInfo.TryCapture();
            if (focus is null)
            {
                Dismiss();
                return;
            }

            if (focus.Hwnd != _lastHwnd)
            {
                _lastHwnd = focus.Hwnd;
            }

            if (ControlClassifier.ShouldSkip(focus.Hwnd))
            {
                Dismiss();
                return;
            }

            // Prefer focus-thread IME state. Root IMC on Win11 Notepad can look "native"
            // while tray ENG / non-native conversion on the focus hwnd allows English.
            if (ImeGate.ShouldSuppressCompletions(focus.Hwnd))
            {
                Dismiss();
                return;
            }

            // Root: only suppress on active composition (not layout/native alone).
            if (focus.RootHwnd != IntPtr.Zero &&
                focus.RootHwnd != focus.Hwnd &&
                ImeGate.IsCompositionActive(focus.RootHwnd))
            {
                Dismiss();
                return;
            }

            if (focus.HasSelection)
            {
                Dismiss();
                return;
            }

            CompletionSuggestion? suggestion;
            if (wordEnd)
            {
                // After word-end: correction marks win (no mid-word completion).
                if (string.IsNullOrEmpty(focus.BeforeCaret))
                {
                    Dismiss();
                    return;
                }

                suggestion = _engine.SuggestMark(focus.BeforeCaret);
                if (suggestion is null)
                {
                    Dismiss();
                    return;
                }
            }
            else
            {
                // Mid-word: completion wins.
                if (string.IsNullOrEmpty(focus.Prefix) ||
                    focus.Prefix.Length < _engine.MinPrefixLength)
                {
                    Dismiss();
                    return;
                }

                suggestion = _engine.Suggest(focus.Prefix);
                if (suggestion is null)
                {
                    Dismiss();
                    return;
                }
            }

            lock (_gate)
            {
                _current = suggestion;
                _pendingWordEnd = false;
            }

            // Always show when prefix+suggestion exist. Weak FontProbe → overlay defaults.
            var caretHeightPx = focus.CaretHeightPx > 0
                ? focus.CaretHeightPx
                : focus.TargetCellHeightPx;
            var fontSizeDip = focus.FontSizeDip is > 0 ? focus.FontSizeDip : null;
            _overlay.Show(
                suggestion,
                focus.CaretScreen,
                caretHeightPx,
                focus.Dpi,
                fontSizeDip,
                showUnderline: suggestion.IsCorrectionMark);
        }
        catch
        {
            Dismiss();
        }
    }

    private bool IsImeSuppressing()
    {
        try
        {
            var hwnd = Native.NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (ImeGate.ShouldSuppressCompletions(hwnd))
            {
                return true;
            }

            var root = Native.NativeMethods.GetAncestor(hwnd, Native.NativeMethods.GA_ROOT);
            return root != IntPtr.Zero &&
                   root != hwnd &&
                   ImeGate.IsCompositionActive(root);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDown(int vk) => (Native.NativeMethods.GetKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _hook.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
