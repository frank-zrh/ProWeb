using System.Collections.Concurrent;

namespace ProWeb.Shared.Crypto;

/// <summary>
/// Anti-replay protection based on a timestamp window plus a bounded set of seen RequestIds.
/// A request is rejected if its timestamp is outside the allowed skew window, or if its
/// RequestId has already been observed within that window.
/// <para>
/// The tracking set is bounded: expired entries are swept amortized (every
/// <see cref="SweepIntervalOps"/> operations, not on every call) so a single <see cref="TryAccept"/>
/// no longer scans the whole set, and a hard capacity cap evicts the oldest entries (LRU) so memory
/// stays bounded even under a flood of unique RequestIds within one window.
/// </para>
/// </summary>
public sealed class ReplayGuard
{
    private const int SweepIntervalOps = 1024;

    private readonly long _windowMs;
    private readonly int _maxEntries;
    private readonly ConcurrentDictionary<string, long> _seen = new();
    private readonly Func<long> _now;
    private int _opsSinceSweep;

    public ReplayGuard(long windowMs = 300_000, Func<long>? nowProvider = null, int maxEntries = 200_000)
    {
        if (windowMs <= 0) throw new ArgumentOutOfRangeException(nameof(windowMs));
        if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        _windowMs = windowMs;
        _maxEntries = maxEntries;
        _now = nowProvider ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// Validates a request. Returns true if fresh and unique; false if expired or a replay.
    /// </summary>
    public bool TryAccept(string requestId, long timestampUnixMs)
    {
        if (string.IsNullOrEmpty(requestId)) return false;
        var now = _now();
        if (Math.Abs(now - timestampUnixMs) > _windowMs)
            return false;

        MaybeSweep(now);

        // If the id was seen recently (still inside the window) it is a replay. If it was seen but
        // has since expired, treat it as fresh and refresh its timestamp.
        if (_seen.TryGetValue(requestId, out var existing))
        {
            if (Math.Abs(now - existing) <= _windowMs)
                return false;
            _seen[requestId] = timestampUnixMs;
            return true;
        }

        if (!_seen.TryAdd(requestId, timestampUnixMs))
            return false;

        if (_seen.Count > _maxEntries)
            EnforceCapacity(now);

        return true;
    }

    /// <summary>Count of entries still within the freshness window (diagnostic only).</summary>
    public int TrackedCount
    {
        get
        {
            var now = _now();
            var count = 0;
            foreach (var kvp in _seen)
            {
                if (Math.Abs(now - kvp.Value) <= _windowMs)
                    count++;
            }

            return count;
        }
    }

    private void MaybeSweep(long now)
    {
        // Amortize eviction: only sweep every N operations so per-call cost is O(1) amortized.
        if (Interlocked.Increment(ref _opsSinceSweep) < SweepIntervalOps)
            return;
        Interlocked.Exchange(ref _opsSinceSweep, 0);
        SweepExpired(now);
    }

    private void SweepExpired(long now)
    {
        foreach (var kvp in _seen)
        {
            if (Math.Abs(now - kvp.Value) > _windowMs)
                _seen.TryRemove(kvp.Key, out _);
        }
    }

    private void EnforceCapacity(long now)
    {
        SweepExpired(now);
        if (_seen.Count <= _maxEntries)
            return;

        // Still over capacity: evict the oldest entries (smallest timestamp) until under the cap.
        var overflow = _seen.Count - _maxEntries;
        foreach (var kvp in _seen.OrderBy(k => k.Value).Take(overflow))
            _seen.TryRemove(kvp.Key, out _);
    }
}
