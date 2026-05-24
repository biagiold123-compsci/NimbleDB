using System.Collections.Concurrent;
using NimbleDB.Core;
using NimbleDB.Exceptions;
using NimbleDB.Observability;
using NimbleDB.Query;
using NimbleDB.Serialization;
using NimbleDB.Storage;

namespace NimbleDB;

/// <summary>
/// Top-level database facade. Thread-safe at the catalog level.
/// </summary>
public sealed class Database : IDisposable
{
    private readonly ConcurrentDictionary<string, TableEngine> _tables =
        new(StringComparer.OrdinalIgnoreCase);

    public string Name { get; }

    public Database(string name) { Name = name; }

    // ── DDL ────────────────────────────────────────────────────────────────────
    public TableEngine CreateTable(TableSchema schema)
    {
        var engine = new TableEngine(schema);
        if (!_tables.TryAdd(schema.TableName, engine))
            throw new TableAlreadyExistsException(schema.TableName);
        EventBus.Instance.Publish(new DbEvent
        {
            Kind = DbEventKind.TableCreated,
            TableName = schema.TableName,
            Detail = $"{schema.Columns.Count} columns"
        });
        return engine;
    }

    public bool DropTable(string name)
    {
        if (!_tables.TryRemove(name, out var engine))
            throw new TableNotFoundException(name);
        engine.Dispose();
        EventBus.Instance.Publish(new DbEvent { Kind = DbEventKind.TableDropped, TableName = name });
        return true;
    }

    public TableEngine GetTable(string name) =>
        _tables.TryGetValue(name, out var e) ? e : throw new TableNotFoundException(name);

    public IEnumerable<string> TableNames => _tables.Keys;

    // ── DML via query language ─────────────────────────────────────────────────
    public ResultSet Query(string sql)
    {
        var stmt = new QueryParser(sql).Parse();
        return new QueryExecutor(new Dictionary<string, TableEngine>(_tables, StringComparer.OrdinalIgnoreCase)).Execute(stmt);
    }

    public IReadOnlyList<ResultSet> QueryBatch(string batch)
    {
        return SplitStatements(batch)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(Query)
            .ToList();
    }

    // ── Persistence ────────────────────────────────────────────────────────────
    public void SaveTo(string path) => DatabaseSerializer.Save(path, _tables);

    public void LoadFrom(string path)
    {
        foreach (var (name, engine) in DatabaseSerializer.Load(path))
            _tables[name] = engine;
    }

    // ── Stats ──────────────────────────────────────────────────────────────────
    public void PrintStats()
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"\n  Database : {Name}");
        Console.WriteLine($"  Tables   : {_tables.Count}");
        Console.ResetColor();
        foreach (var (name, engine) in _tables.OrderBy(kv => kv.Key))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"    • {name,-20} {engine.Count(),6:N0} rows   {engine.Schema.Columns.Count} columns");
        }
        Console.ResetColor();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private static IEnumerable<string> SplitStatements(string batch)
    {
        var stmts = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inString = false;
        foreach (char ch in batch)
        {
            if (ch == '\'' && !inString) inString = true;
            else if (ch == '\'' && inString) inString = false;
            else if (ch == ';' && !inString) { stmts.Add(current.ToString()); current.Clear(); continue; }
            current.Append(ch);
        }
        if (current.Length > 0) stmts.Add(current.ToString());
        return stmts;
    }

    public void Dispose()
    {
        foreach (var engine in _tables.Values) engine.Dispose();
        _tables.Clear();
    }
}
