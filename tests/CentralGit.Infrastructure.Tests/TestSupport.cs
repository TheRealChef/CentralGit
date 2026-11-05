namespace CentralGit.Infrastructure.Tests;

/// <summary>A clock stopped at one moment, for ages and "last 30 days" in tests.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
