using ChisBallot.Data;

namespace ChisBallot.Tests;

public sealed class AttemptCounterTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => this.Now;
    }

    [Fact]
    public void A_key_is_blocked_after_its_limit_and_free_again_when_the_window_passes()
    {
        Clock clock = new();
        AttemptCounter counter = new(limit: 3, window: TimeSpan.FromMinutes(10), clock);

        counter.Add("a");
        counter.Add("a");
        Assert.False(counter.IsBlocked("a"));
        counter.Add("a");
        Assert.True(counter.IsBlocked("a"));

        clock.Now += TimeSpan.FromMinutes(10);
        Assert.False(counter.IsBlocked("a"));
    }

    [Fact]
    public void Keys_do_not_affect_each_other()
    {
        AttemptCounter counter = new(limit: 1, window: TimeSpan.FromMinutes(1), new Clock());

        counter.Add("a");

        Assert.True(counter.IsBlocked("a"));
        Assert.False(counter.IsBlocked("b"));
    }

    [Fact]
    public void TryAdd_refuses_once_the_limit_is_reached_and_does_not_count_the_refusal()
    {
        Clock clock = new();
        AttemptCounter counter = new(limit: 2, window: TimeSpan.FromMinutes(10), clock);

        Assert.True(counter.TryAdd("a"));
        clock.Now += TimeSpan.FromMinutes(6);
        Assert.True(counter.TryAdd("a"));
        Assert.False(counter.TryAdd("a"));

        // The first event leaves the window at minute 10; a refusal added nothing, so one slot is free.
        clock.Now += TimeSpan.FromMinutes(4);
        Assert.True(counter.TryAdd("a"));
    }
}
