using System.Windows.Threading;
using WinGhostText.App.Input;
using WinGhostText.App.Ui;
using WinGhostText.Core;

namespace WinGhostText.App.Services;

/// <summary>
/// Orchestrates hooks, gates, trie suggestions, overlay, and accept/dismiss.
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

    private CompletionSuggestion? _current;
    private IntPtr _lastHwnd;
    private bool _enabled = true;
    private bool _suppressHook;
    private bool _disposed;

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
            RefreshSuggestion();
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
                    e.Handled = true;
                    Accept();
                }

                return;

            case VirtualKeys.Space:
                // Space never accepts GhostText — insert a normal space and end the suggestion.
                Dismiss();
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
            ScheduleRefresh();
            return;
        }

        if (e.VirtualKey is (>= 0x30 and <= 0x39) || e.VirtualKey is (>= 0xBA and <= 0xDF))
        {
            Dismiss();
        }
    }

    private void Accept()
    {
        CompletionSuggestion? suggestion;
        lock (_gate)
        {
            suggestion = _current;
            _current = null;
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
        }

        _overlay.Hide();
    }

    private void ScheduleRefresh()
    {
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

    private void RefreshSuggestion()
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

            if (ImeGate.IsCompositionActive(focus.Hwnd) ||
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

            if (string.IsNullOrEmpty(focus.Prefix) ||
                focus.Prefix.Length < _engine.MinPrefixLength)
            {
                Dismiss();
                return;
            }

            var suggestion = _engine.Suggest(focus.Prefix);
            if (suggestion is null)
            {
                Dismiss();
                return;
            }

            lock (_gate)
            {
                _current = suggestion;
            }

            var cellPx = focus.TargetCellHeightPx > 0 ? focus.TargetCellHeightPx : focus.CaretHeightPx;
            _overlay.Show(
                suggestion,
                focus.CaretScreen,
                focus.FontFamily,
                focus.FontSizeDip,
                focus.FontWeight,
                cellPx);
        }
        catch
        {
            Dismiss();
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
