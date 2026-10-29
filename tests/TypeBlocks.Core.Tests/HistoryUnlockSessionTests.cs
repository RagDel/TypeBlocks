using TypeBlocks.Core;

internal static class HistoryUnlockSessionTests
{
    private const string Password = "Synthetic unlock password 42!";

    public static void Verify(Action<string, Action> run)
    {
        run("unlock session starts locked and holds credentials only after unlock", () =>
        {
            var session = new HistoryUnlockSession(new TestClock());
            Assert(session.LockDuration == TimeSpan.FromHours(1));
            Assert(!session.IsUnlocked && session.Version == 0 && session.ExpiresAtUtc is null);
            Assert(session.Remaining == TimeSpan.Zero && !session.TryGetPassword(out var missing) && missing is null);
            session.Unlock(Password);
            Assert(session.Version == 1 && session.Remaining == TimeSpan.FromHours(1));
            Assert(session.TryGetPassword(out var password) && password == Password);
        });
        run("unlock session expires exactly one hour after authentication", () =>
        {
            var clock = new TestClock();
            var session = new HistoryUnlockSession(clock);
            int locked = 0;
            session.Locked += () => locked++;
            session.Unlock(Password);
            var deadline = clock.GetUtcNow() + TimeSpan.FromHours(1);
            Assert(session.ExpiresAtUtc == deadline);
            clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromTicks(1));
            Assert(session.IsUnlocked && session.Remaining == TimeSpan.FromTicks(1));
            clock.Advance(TimeSpan.FromTicks(1));
            Assert(!session.TryGetPassword(out var expired) && expired is null);
            Assert(session.ExpiresAtUtc is null && session.Remaining == TimeSpan.Zero && session.Version == 2 && locked == 1);
            clock.Advance(TimeSpan.FromHours(1));
            session.CheckExpiry();
            Assert(!session.IsUnlocked && locked == 1);
        });
        run("unlock session access does not slide or renew the deadline", () =>
        {
            var clock = new TestClock();
            var session = new HistoryUnlockSession(clock);
            session.Unlock(Password);
            var deadline = session.ExpiresAtUtc;
            long version = session.Version;
            for (int minute = 1; minute < 60; minute++)
            {
                clock.Advance(TimeSpan.FromMinutes(1));
                Assert(session.TryGetPassword(out _) && session.ExpiresAtUtc == deadline && session.Version == version);
                Assert(session.Remaining == TimeSpan.FromMinutes(60 - minute));
            }
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert(!session.IsUnlocked);
        });
        run("unlock session timer clears credentials with no viewer or access", () =>
        {
            var clock = new TestClock();
            var session = new HistoryUnlockSession(clock);
            int locked = 0;
            session.Locked += () => locked++;
            session.Unlock(Password);
            clock.Advance(TimeSpan.FromHours(2));
            session.CheckExpiry();
            Assert(locked == 1 && !session.TryGetPassword(out var expired) && expired is null);
        });
        run("unlock session manual lock is immediate, idempotent, and observable", () =>
        {
            var session = new HistoryUnlockSession(new TestClock());
            int locked = 0;
            session.Locked += () =>
            {
                locked++;
                Assert(!session.IsUnlocked && session.ExpiresAtUtc is null && session.Remaining == TimeSpan.Zero);
            };
            session.Lock();
            Assert(session.Version == 0 && locked == 0);
            session.Unlock(Password);
            session.Lock();
            session.Lock();
            Assert(session.Version == 2 && locked == 1 && !session.TryGetPassword(out _));
        });
        run("unlock session new authentication starts a new fixed hour", () =>
        {
            var clock = new TestClock();
            var session = new HistoryUnlockSession(clock);
            session.Unlock(Password);
            clock.Advance(TimeSpan.FromMinutes(45));
            long oldVersion = session.Version;
            const string changed = "Another synthetic password 73!";
            session.Unlock(changed);
            Assert(session.Version == oldVersion + 1 && session.Remaining == TimeSpan.FromHours(1));
            clock.Advance(TimeSpan.FromMinutes(15));
            Assert(session.TryGetPassword(out var current) && current == changed);
            clock.Advance(TimeSpan.FromMinutes(45));
            Assert(!session.IsUnlocked);
        });
        run("unlock session clock rollback cannot extend its lifetime", () =>
        {
            var clock = new TestClock();
            var session = new HistoryUnlockSession(clock);
            session.Unlock(Password);
            clock.Advance(TimeSpan.FromMinutes(30));
            clock.ShiftUtc(TimeSpan.FromDays(-1));
            Assert(session.Remaining == TimeSpan.FromMinutes(30));
            clock.Advance(TimeSpan.FromMinutes(30));
            Assert(!session.IsUnlocked);
        });
        run("unlock session wall deadline expires after suspend or clock advance", () =>
        {
            var clock = new TestClock();
            var session = new HistoryUnlockSession(clock);
            session.Unlock(Password);
            clock.ShiftUtc(TimeSpan.FromHours(1));
            // The monotonic test clock has not advanced: wall time alone must revoke access.
            Assert(session.ExpiresAtUtc is null && !session.IsUnlocked);
        });
        run("unlock session version and remaining queries also enforce expiry", () =>
        {
            var clock = new TestClock();
            var session = new HistoryUnlockSession(clock);
            int locked = 0;
            session.Locked += () => locked++;
            session.Unlock(Password);
            clock.Advance(TimeSpan.FromHours(1));
            Assert(session.Version == 2 && locked == 1);
            session.Unlock(Password);
            clock.Advance(TimeSpan.FromHours(1));
            Assert(session.Remaining == TimeSpan.Zero && locked == 2 && session.Version == 4);
        });
        run("unlock session rejects empty credentials without replacing an active grant", () =>
        {
            var session = new HistoryUnlockSession(new TestClock());
            session.Unlock(Password);
            bool rejected = false;
            try { session.Unlock(""); } catch (ArgumentException) { rejected = true; }
            Assert(rejected && session.Version == 1 && session.TryGetPassword(out var current) && current == Password);
        });
        run("custom unlock duration expires exactly at its fixed deadline", () =>
        {
            var clock = new TestClock();
            var session = new HistoryUnlockSession(clock);
            session.SetDuration(TimeSpan.FromMinutes(15));
            session.Unlock(Password);
            var deadline = clock.GetUtcNow() + TimeSpan.FromMinutes(15);
            Assert(session.LockDuration == TimeSpan.FromMinutes(15) && session.ExpiresAtUtc == deadline);
            clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromTicks(1));
            Assert(session.IsUnlocked && session.Remaining == TimeSpan.FromTicks(1));
            clock.Advance(TimeSpan.FromTicks(1));
            Assert(!session.IsUnlocked && session.ExpiresAtUtc is null);
        });
        run("unlock duration accepts its bounds and rejects invalid changes without losing access", () =>
        {
            var session = new HistoryUnlockSession(new TestClock());
            session.SetDuration(TimeSpan.FromMinutes(1));
            session.Unlock(Password);
            Assert(session.Remaining == TimeSpan.FromMinutes(1));
            session.SetDuration(TimeSpan.FromDays(1));
            var deadline = session.ExpiresAtUtc;
            long version = session.Version;
            foreach (var invalid in new[] { TimeSpan.Zero, TimeSpan.FromMinutes(-1), TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1), TimeSpan.FromDays(1) + TimeSpan.FromTicks(1), TimeSpan.MaxValue })
            {
                bool rejected = false;
                try { session.SetDuration(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Assert(rejected && session.LockDuration == TimeSpan.FromDays(1) && session.ExpiresAtUtc == deadline && session.Version == version);
                Assert(session.TryGetPassword(out var current) && current == Password);
            }
        });
        run("shortening unlock duration preserves elapsed time and can lock immediately", () =>
        {
            var clock = new TestClock();
            var session = new HistoryUnlockSession(clock);
            int locked = 0;
            session.Locked += () => locked++;
            session.Unlock(Password);
            var authenticatedAt = clock.GetUtcNow();
            clock.Advance(TimeSpan.FromMinutes(10));
            session.SetDuration(TimeSpan.FromMinutes(20));
            Assert(session.Remaining == TimeSpan.FromMinutes(10) && session.ExpiresAtUtc == authenticatedAt + TimeSpan.FromMinutes(20));
            clock.Advance(TimeSpan.FromMinutes(2));
            session.SetDuration(TimeSpan.FromMinutes(20));
            Assert(session.Remaining == TimeSpan.FromMinutes(8));
            session.SetDuration(TimeSpan.FromMinutes(5));
            Assert(!session.IsUnlocked && !session.TryGetPassword(out _) && session.Remaining == TimeSpan.Zero && locked == 1);
        });
        run("lengthening unlock duration remains anchored to the original authentication", () =>
        {
            var clock = new TestClock();
            var session = new HistoryUnlockSession(clock);
            session.Unlock(Password);
            var authenticatedAt = clock.GetUtcNow();
            clock.Advance(TimeSpan.FromMinutes(30));
            long version = session.Version;
            session.SetDuration(TimeSpan.FromMinutes(120));
            Assert(session.Remaining == TimeSpan.FromMinutes(90) && session.ExpiresAtUtc == authenticatedAt + TimeSpan.FromMinutes(120));
            Assert(session.Version == version);
            clock.Advance(TimeSpan.FromMinutes(90));
            Assert(!session.IsUnlocked);
        });
        run("changing duration cannot revive an expired unlock and applies to the next unlock", () =>
        {
            var clock = new TestClock();
            var session = new HistoryUnlockSession(clock);
            session.Unlock(Password);
            clock.Advance(TimeSpan.FromMinutes(61));
            session.SetDuration(TimeSpan.FromMinutes(120));
            Assert(!session.IsUnlocked && session.Version == 2 && session.ExpiresAtUtc is null);
            session.Unlock(Password);
            Assert(session.Remaining == TimeSpan.FromMinutes(120) && session.Version == 3);
        });
        run("custom duration changes preserve monotonic protection against clock rollback", () =>
        {
            var clock = new TestClock();
            var session = new HistoryUnlockSession(clock);
            session.SetDuration(TimeSpan.FromMinutes(30));
            session.Unlock(Password);
            clock.Advance(TimeSpan.FromMinutes(15));
            clock.ShiftUtc(TimeSpan.FromDays(-1));
            session.SetDuration(TimeSpan.FromMinutes(60));
            Assert(session.Remaining == TimeSpan.FromMinutes(45));
            clock.Advance(TimeSpan.FromMinutes(45));
            Assert(!session.IsUnlocked);
        });
        run("custom duration changes retain wall time already spent suspended", () =>
        {
            var clock = new TestClock();
            var session = new HistoryUnlockSession(clock);
            session.SetDuration(TimeSpan.FromMinutes(30));
            session.Unlock(Password);
            clock.Advance(TimeSpan.FromMinutes(5));
            clock.ShiftUtc(TimeSpan.FromMinutes(20));
            session.SetDuration(TimeSpan.FromMinutes(60));
            Assert(session.Remaining == TimeSpan.FromMinutes(35));
            clock.ShiftUtc(TimeSpan.FromMinutes(35));
            Assert(!session.IsUnlocked);
        });
    }

    private static void Assert(bool condition)
    {
        if (!condition) throw new Exception("Unlock session assertion failed.");
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2031, 1, 2, 12, 0, 0, TimeSpan.Zero);
        private long _timestamp;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan amount) { _utcNow += amount; _timestamp += amount.Ticks; }
        public void ShiftUtc(TimeSpan amount) => _utcNow += amount;
    }
}
