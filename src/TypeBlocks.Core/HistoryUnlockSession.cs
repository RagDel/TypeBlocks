using System.Diagnostics.CodeAnalysis;

namespace TypeBlocks.Core;

/// <summary>
/// A fixed, in-memory history unlock. The caller verifies the password before Unlock and
/// calls CheckExpiry from its UI timer even when no history window is open.
/// Use on the owning UI thread; this object never writes credentials to disk.
/// </summary>
public sealed class HistoryUnlockSession(TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan Duration = TimeSpan.FromHours(1);
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromDays(1);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private string? _password;
    private TimeSpan _duration = Duration;
    private DateTimeOffset _startedAtUtc;
    private DateTimeOffset? _expiresAtUtc;
    private long _startedTimestamp;
    private long _version;

    public event Action? Locked;

    public TimeSpan LockDuration => _duration;
    public bool IsUnlocked => TryGetPassword(out _);
    public DateTimeOffset? ExpiresAtUtc { get { CheckExpiry(); return _expiresAtUtc; } }
    public long Version { get { CheckExpiry(); return _version; } }
    public TimeSpan Remaining
    {
        get
        {
            if (_password is null) return TimeSpan.Zero;
            TimeSpan remaining = GetRemaining();
            if (remaining > TimeSpan.Zero) return remaining;
            Lock();
            return TimeSpan.Zero;
        }
    }

    public void Unlock(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        _startedTimestamp = _timeProvider.GetTimestamp();
        _startedAtUtc = _timeProvider.GetUtcNow();
        _expiresAtUtc = _startedAtUtc + _duration;
        _password = password;
        _version++;
    }

    /// <summary>Changes the fixed duration without renewing authentication or its start time.</summary>
    public void SetDuration(TimeSpan duration)
    {
        if (duration < MinimumDuration || duration > MaximumDuration)
            throw new ArgumentOutOfRangeException(nameof(duration), "Use a lock duration between 1 minute and 24 hours.");
        // A setting change must never revive credentials whose previous deadline passed.
        CheckExpiry();
        _duration = duration;
        if (_password is null) return;
        _expiresAtUtc = _startedAtUtc + _duration;
        CheckExpiry();
    }

    public bool TryGetPassword([NotNullWhen(true)] out string? password)
    {
        CheckExpiry();
        password = _password;
        return password is not null;
    }

    public void CheckExpiry()
    {
        if (_password is not null && GetRemaining() <= TimeSpan.Zero) Lock();
    }

    public void Lock()
    {
        if (_password is null) return;
        _password = null;
        _expiresAtUtc = null;
        _startedAtUtc = default;
        _startedTimestamp = 0;
        _version++;
        Locked?.Invoke();
    }

    private TimeSpan GetRemaining()
    {
        // Wall time covers suspend/resume; monotonic elapsed time prevents clock rollback
        // from extending an unlock. Neither access nor window activity resets either deadline.
        TimeSpan elapsed = _timeProvider.GetElapsedTime(_startedTimestamp);
        if (elapsed < TimeSpan.Zero) return TimeSpan.Zero;
        TimeSpan elapsedRemaining = _duration - elapsed;
        TimeSpan wallRemaining = _expiresAtUtc!.Value - _timeProvider.GetUtcNow();
        return elapsedRemaining < wallRemaining ? elapsedRemaining : wallRemaining;
    }
}
