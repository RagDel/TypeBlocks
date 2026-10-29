namespace TypeBlocks.Core;

public sealed record KeystrokeEntry(
    DateTimeOffset Timestamp, string Application, string Key, string? Text, bool IsRepeat);
