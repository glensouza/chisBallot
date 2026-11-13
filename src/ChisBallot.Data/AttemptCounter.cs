namespace ChisBallot.Data;

/// <summary>
/// Counts events per key inside a sliding window, in memory only. Used to slow
/// down someone guessing codes or asking for demo slips in bulk. A key is whatever
/// the caller passes (the client's address); nothing is written to disk or logs, and
/// an entry disappears when its window has passed.
/// </summary>
public sealed class AttemptCounter(int limit, TimeSpan window, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly Dictionary<string, Queue<DateTimeOffset>> events = [];
    private readonly Lock gate = new();

    /// <summary>True when the key has already used up its allowance.</summary>
    public bool IsBlocked(string key)
    {
        lock (this.gate)
        {
            return this.Prune(key) >= limit;
        }
    }

    /// <summary>Records one event for the key.</summary>
    public void Add(string key)
    {
        lock (this.gate)
        {
            this.Prune(key);
            if (!this.events.TryGetValue(key, out Queue<DateTimeOffset>? queue))
            {
                queue = new Queue<DateTimeOffset>();
                this.events[key] = queue;
            }

            queue.Enqueue(this.clock.GetUtcNow());
        }
    }

    /// <summary>Records an event unless the key is already at its limit. False means refused.</summary>
    public bool TryAdd(string key)
    {
        lock (this.gate)
        {
            if (this.Prune(key) >= limit)
            {
                return false;
            }

            this.Add(key);
            return true;
        }
    }

    // Drops events older than the window, forgets an empty key, returns what is left.
    private int Prune(string key)
    {
        if (!this.events.TryGetValue(key, out Queue<DateTimeOffset>? queue))
        {
            return 0;
        }

        DateTimeOffset cutoff = this.clock.GetUtcNow() - window;
        while (queue.Count != 0 && queue.Peek() <= cutoff)
        {
            queue.Dequeue();
        }

        if (queue.Count == 0)
        {
            this.events.Remove(key);
        }

        return queue.Count;
    }
}
