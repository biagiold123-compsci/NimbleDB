namespace NimbleDB.Observability;

public enum DbEventKind
{
    TableCreated, TableDropped,
    RowInserted, RowUpdated, RowDeleted,
    QueryExecuted, TransactionCommitted, TransactionRolledBack
}

public sealed class DbEvent
{
    public DbEventKind Kind { get; init; }
    public string TableName { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public long ElapsedMs { get; init; }

    public override string ToString() =>
        $"[{OccurredAt:HH:mm:ss.fff}] {Kind,-24} table={TableName,-20} {Detail} ({ElapsedMs}ms)";
}

public interface IDbObserver
{
    void OnEvent(DbEvent dbEvent);
}

public sealed class EventBus
{
    private static readonly Lazy<EventBus> _instance =
        new(() => new EventBus(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static EventBus Instance => _instance.Value;

    private readonly List<IDbObserver> _observers = [];
    private readonly object _lock = new();

    private EventBus() { }

    public void Subscribe(IDbObserver observer)
    {
        lock (_lock) _observers.Add(observer);
    }

    public void Unsubscribe(IDbObserver observer)
    {
        lock (_lock) _observers.Remove(observer);
    }

    public void Publish(DbEvent evt)
    {
        IDbObserver[] snapshot;
        lock (_lock) snapshot = [.. _observers];
        foreach (var obs in snapshot)
        {
            try { obs.OnEvent(evt); }
            catch { /* observer failures must not crash the engine */ }
        }
    }
}

public sealed class ConsoleLogger : IDbObserver
{
    private static ConsoleColor ColorFor(DbEventKind kind) => kind switch
    {
        DbEventKind.RowInserted   => ConsoleColor.Green,
        DbEventKind.RowDeleted    => ConsoleColor.Red,
        DbEventKind.RowUpdated    => ConsoleColor.Yellow,
        DbEventKind.QueryExecuted => ConsoleColor.Cyan,
        DbEventKind.TableCreated  => ConsoleColor.Magenta,
        DbEventKind.TableDropped  => ConsoleColor.DarkRed,
        _                         => ConsoleColor.Gray
    };

    public void OnEvent(DbEvent dbEvent)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ColorFor(dbEvent.Kind);
        Console.WriteLine($"  {dbEvent}");
        Console.ForegroundColor = prev;
    }
}

public sealed class MetricsCollector : IDbObserver
{
    private long _inserts, _updates, _deletes, _queries, _totalQueryMs;

    public void OnEvent(DbEvent evt)
    {
        switch (evt.Kind)
        {
            case DbEventKind.RowInserted:  Interlocked.Increment(ref _inserts); break;
            case DbEventKind.RowUpdated:   Interlocked.Increment(ref _updates); break;
            case DbEventKind.RowDeleted:   Interlocked.Increment(ref _deletes); break;
            case DbEventKind.QueryExecuted:
                Interlocked.Increment(ref _queries);
                Interlocked.Add(ref _totalQueryMs, evt.ElapsedMs);
                break;
        }
    }

    public void PrintSummary()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  ┌─────────────────────────────────┐");
        Console.WriteLine("  │         SESSION METRICS          │");
        Console.WriteLine("  ├─────────────────────────────────┤");
        Console.WriteLine($"  │  Inserts:        {_inserts,14:N0} │");
        Console.WriteLine($"  │  Updates:        {_updates,14:N0} │");
        Console.WriteLine($"  │  Deletes:        {_deletes,14:N0} │");
        Console.WriteLine($"  │  Queries:        {_queries,14:N0} │");
        double avgMs = _queries > 0 ? (double)_totalQueryMs / _queries : 0;
        Console.WriteLine($"  │  Avg Query (ms): {avgMs,14:F2} │");
        Console.WriteLine("  └─────────────────────────────────┘");
        Console.ResetColor();
    }
}
