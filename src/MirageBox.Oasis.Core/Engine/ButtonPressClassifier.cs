namespace MirageBox.Oasis.Core.Engine;

public enum PressKind { Single, Double, Hold }

/// <summary>
/// Classifies raw press/release edges into Single, Double, and Hold presses.
///
/// State machine per button:
/// - press: start the hold timer. If it fires while still held → Hold
///   (the eventual release is ignored).
/// - release before the hold threshold: a single press is pending. A second
///   press within the double window upgrades it to Double; otherwise the
///   window timer fires Single.
///
/// Callers that know a button has no double/hold actions should bypass this
/// classifier and fire on the press edge directly — that path has zero latency.
/// </summary>
public sealed class ButtonPressClassifier : IDisposable
{
    public static readonly TimeSpan HoldThreshold = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DoubleWindow = TimeSpan.FromMilliseconds(300);

    private readonly Action<int, PressKind> _onClassified;
    private readonly Dictionary<int, ButtonState> _states = new();
    private readonly object _gate = new();
    private bool _disposed;

    private sealed class ButtonState
    {
        public bool IsDown;
        public bool HoldFired;
        public bool DoubleFired;
        public bool SinglePending;
        public Timer? HoldTimer;
        public Timer? DoubleTimer;
    }

    public ButtonPressClassifier(Action<int, PressKind> onClassified)
    {
        _onClassified = onClassified;
    }

    public void OnEdge(int buttonIndex, bool isPressed)
    {
        PressKind? fire = null;
        lock (_gate)
        {
            if (_disposed) return;
            var s = GetState(buttonIndex);

            if (isPressed)
            {
                s.IsDown = true;
                s.HoldFired = false;

                if (s.SinglePending)
                {
                    // Second press inside the double window.
                    s.SinglePending = false;
                    s.DoubleFired = true;
                    s.DoubleTimer?.Dispose();
                    s.DoubleTimer = null;
                    fire = PressKind.Double;
                }
                else
                {
                    s.HoldTimer?.Dispose();
                    s.HoldTimer = new Timer(_ => OnHoldElapsed(buttonIndex), null, HoldThreshold, Timeout.InfiniteTimeSpan);
                }
            }
            else
            {
                s.IsDown = false;
                s.HoldTimer?.Dispose();
                s.HoldTimer = null;

                if (s.HoldFired) return;          // hold already consumed this press
                if (s.DoubleFired) { s.DoubleFired = false; return; }   // release after a Double's second press

                s.SinglePending = true;
                s.DoubleTimer?.Dispose();
                s.DoubleTimer = new Timer(_ => OnDoubleWindowElapsed(buttonIndex), null, DoubleWindow, Timeout.InfiniteTimeSpan);
            }
        }
        if (fire is { } kind)
            _onClassified(buttonIndex, kind);
    }

    private void OnHoldElapsed(int buttonIndex)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var s = GetState(buttonIndex);
            if (!s.IsDown || s.HoldFired) return;
            s.HoldFired = true;
        }
        _onClassified(buttonIndex, PressKind.Hold);
    }

    private void OnDoubleWindowElapsed(int buttonIndex)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var s = GetState(buttonIndex);
            if (!s.SinglePending) return;
            s.SinglePending = false;
            s.DoubleTimer?.Dispose();
            s.DoubleTimer = null;
        }
        _onClassified(buttonIndex, PressKind.Single);
    }

    private ButtonState GetState(int buttonIndex)
    {
        if (!_states.TryGetValue(buttonIndex, out var s))
            _states[buttonIndex] = s = new ButtonState();
        return s;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var s in _states.Values)
            {
                s.HoldTimer?.Dispose();
                s.DoubleTimer?.Dispose();
            }
            _states.Clear();
        }
    }
}
