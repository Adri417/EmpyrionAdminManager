using System.Collections.Concurrent;

namespace Eam.Core.Security;

/// <summary>
/// Slows down password guessing: after 5 failures from the same address within
/// 15 minutes, that address is blocked for 15 minutes.
/// </summary>
public sealed class LoginThrottle(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset FirstFailure;
        public DateTimeOffset? BlockedUntil;
    }

    public bool IsBlocked(string key, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        if (!_entries.TryGetValue(key, out var e)) return false;
        lock (e)
        {
            var now = _time.GetUtcNow();
            if (e.BlockedUntil is { } until && until > now)
            {
                remaining = until - now;
                return true;
            }
            return false;
        }
    }

    public void Fail(string key)
    {
        var e = _entries.GetOrAdd(key, _ => new Entry());
        lock (e)
        {
            var now = _time.GetUtcNow();
            if (e.BlockedUntil is { } until && until <= now) { e.BlockedUntil = null; e.Failures = 0; }
            if (e.Failures == 0 || now - e.FirstFailure > Window) { e.Failures = 0; e.FirstFailure = now; }
            e.Failures++;
            if (e.Failures >= MaxFailures) e.BlockedUntil = now + Window;
        }
        if (_entries.Count > 10_000) Cleanup();
    }

    public void Success(string key) => _entries.TryRemove(key, out _);

    private void Cleanup()
    {
        var now = _time.GetUtcNow();
        foreach (var (k, e) in _entries)
            if ((e.BlockedUntil is null || e.BlockedUntil < now) && now - e.FirstFailure > Window)
                _entries.TryRemove(k, out _);
    }
}
