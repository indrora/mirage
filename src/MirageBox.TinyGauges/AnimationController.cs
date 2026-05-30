using System.Diagnostics;

namespace MirageBox.TinyGauges;

/// <summary>
/// Drives smooth, frame-dropping animation across multiple render slots.
///
/// Each registered slot has an async render callback and an enabled flag.
/// On every tick the controller:
///   1. Computes real elapsed delta time.
///   2. Calls <see cref="OnTick"/> so callers can advance shared animation state
///      (e.g. hue, phase) at the correct wall-clock rate regardless of render load.
///   3. Renders exactly one enabled slot per tick in round-robin order.
///   4. Sleeps only the remaining frame budget — if rendering already used the
///      full budget the next tick starts immediately (frames are dropped, never
///      accumulated).
/// </summary>
public sealed class AnimationController
{
    private readonly record struct Slot(int Id, Func<Task> Render, bool Enabled);

    private readonly List<Slot> _slots = new();
    private readonly object _slotsLock = new();

    /// <summary>
    /// Called at the start of every tick with the elapsed seconds since the last
    /// tick. Use this to advance any animation state that must track wall-clock
    /// time accurately (e.g. <c>hue += (float)(dt * 140.0)</c>).
    /// </summary>
    public Action<double>? OnTick { get; set; }

    /// <summary>Target ticks per second. Default: 60.</summary>
    public double TargetFrameRate { get; set; } = 60.0;

    /// <summary>Registers or replaces a render slot.</summary>
    public void AddSlot(int id, Func<Task> render)
    {
        lock (_slotsLock)
        {
            _slots.RemoveAll(s => s.Id == id);
            _slots.Add(new Slot(id, render, Enabled: true));
        }
    }

    /// <summary>Removes a previously registered slot.</summary>
    public void RemoveSlot(int id)
    {
        lock (_slotsLock) { _slots.RemoveAll(s => s.Id == id); }
    }

    /// <summary>
    /// Enables or disables a slot. Disabled slots are skipped in the round-robin
    /// but remain registered so they can be re-enabled later.
    /// </summary>
    public void SetSlotEnabled(int id, bool enabled)
    {
        lock (_slotsLock)
        {
            int idx = _slots.FindIndex(s => s.Id == id);
            if (idx >= 0)
            {
                var s = _slots[idx];
                _slots[idx] = s with { Enabled = enabled };
            }
        }
    }

    /// <summary>
    /// Runs the animation loop until <paramref name="cancellationToken"/> is
    /// cancelled. Intended to be awaited on a background task.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var lastTick = clock.Elapsed;
        int roundRobinIndex = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var tickStart = clock.Elapsed;
            double dt = (tickStart - lastTick).TotalSeconds;
            lastTick = tickStart;

            // 1. Advance shared animation state.
            OnTick?.Invoke(dt);

            // 2. Pick the next enabled slot in round-robin order.
            Func<Task>? render = null;
            lock (_slotsLock)
            {
                // Build a snapshot of enabled slots to avoid holding the lock
                // across the (potentially slow) render call.
                int total = _slots.Count(s => s.Enabled);
                if (total > 0)
                {
                    roundRobinIndex %= total;
                    int enabledSeen = 0;
                    foreach (var slot in _slots)
                    {
                        if (!slot.Enabled) continue;
                        if (enabledSeen == roundRobinIndex)
                        {
                            render = slot.Render;
                            break;
                        }
                        enabledSeen++;
                    }
                    roundRobinIndex = (roundRobinIndex + 1) % total;
                }
            }

            // 3. Render the chosen slot (outside the lock).
            if (render is not null)
                await render();

            // 4. Sleep only the remaining frame budget; drop the frame if late.
            var frameBudget = TimeSpan.FromSeconds(1.0 / TargetFrameRate);
            var used = clock.Elapsed - tickStart;
            var remaining = frameBudget - used;
            if (remaining > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(remaining, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
