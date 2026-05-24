using NimbleDB.Core;
using NimbleDB.Exceptions;
using NimbleDB.Indexing;
using NimbleDB.Observability;

namespace NimbleDB.Storage;

/// <summary>
/// Thread-safe in-memory table engine using ReaderWriterLockSlim.
/// Supports concurrent reads and exclusive writes with MVCC-lite snapshot semantics.
/// </summary>
public sealed class TableEngine : IDisposable
{
    private sealed class RowSlot
    {
        public volatile Row? Data;
        public volatile int Version;
        public RowSlot(Row data) { Data = data; Version = 1; }
    }

    public TableSchema Schema { get; }

    private readonly List<RowSlot> _slots = [];
    private readonly ReaderWriterLockSlim _rwl = new(LockRecursionPolicy.NoRecursion);
    private readonly HashIndex<object>? _pkIndex;
    private readonly Dictionary<string, HashIndex<object>> _uniqueIndexes = new(StringComparer.OrdinalIgnoreCase);
    private int _nextRowId = 0;

    public TableEngine(TableSchema schema)
    {
        Schema = schema;
        if (schema.PrimaryKey != null)
            _pkIndex = new HashIndex<object>(schema.PrimaryKey.Name);
        foreach (var col in schema.Columns.Where(c => c.Unique && !c.PrimaryKey))
            _uniqueIndexes[col.Name] = new HashIndex<object>(col.Name);
    }

    public int Insert(Row row)
    {
        ValidateRow(row);
        _rwl.EnterWriteLock();
        try
        {
            int rowId = _nextRowId++;
            _slots.Add(new RowSlot(row));
            IndexRow(rowId, row, adding: true);
            Publish(DbEventKind.RowInserted, $"rowId={rowId}");
            return rowId;
        }
        finally { _rwl.ExitWriteLock(); }
    }

    public int BulkInsert(IEnumerable<Row> rows)
    {
        var list = rows.ToList();
        list.ForEach(ValidateRow);
        _rwl.EnterWriteLock();
        try
        {
            int count = 0;
            foreach (var row in list)
            {
                int rowId = _nextRowId++;
                _slots.Add(new RowSlot(row));
                IndexRow(rowId, row, adding: true);
                count++;
            }
            Publish(DbEventKind.RowInserted, $"bulk count={count}");
            return count;
        }
        finally { _rwl.ExitWriteLock(); }
    }

    public IEnumerable<(int RowId, Row Row)> Scan(Func<Row, bool>? predicate = null)
    {
        List<RowSlot> snapshot;
        _rwl.EnterReadLock();
        try { snapshot = [.. _slots]; }
        finally { _rwl.ExitReadLock(); }

        for (int i = 0; i < snapshot.Count; i++)
        {
            var data = snapshot[i].Data;
            if (data is null) continue;
            if (predicate is null || predicate(data))
                yield return (i, data);
        }
    }

    public int Update(Func<Row, bool> predicate, Func<Row, Row> transform)
    {
        _rwl.EnterWriteLock();
        try
        {
            int count = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                var current = slot.Data;
                if (current is null || !predicate(current)) continue;

                var updated = transform((Row)current.Clone());
                ValidateRow(updated);
                IndexRow(i, current, adding: false);
                IndexRow(i, updated, adding: true);
                slot.Data = updated;
                slot.Version++;
                count++;
            }
            if (count > 0) Publish(DbEventKind.RowUpdated, $"updated={count}");
            return count;
        }
        finally { _rwl.ExitWriteLock(); }
    }

    public int Delete(Func<Row, bool> predicate)
    {
        _rwl.EnterWriteLock();
        try
        {
            int count = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                var current = slot.Data;
                if (current is null || !predicate(current)) continue;
                IndexRow(i, current, adding: false);
                slot.Data = null;
                count++;
            }
            if (count > 0) Publish(DbEventKind.RowDeleted, $"deleted={count}");
            return count;
        }
        finally { _rwl.ExitWriteLock(); }
    }

    public int Count()
    {
        _rwl.EnterReadLock();
        try { return _slots.Count(s => s.Data != null); }
        finally { _rwl.ExitReadLock(); }
    }

    private void ValidateRow(Row row)
    {
        foreach (var col in Schema.Columns)
        {
            row.TryGetValue(col.Name, out var val);
            if (!col.Nullable && val is null)
                throw new NullConstraintViolationException(Schema.TableName, col.Name);
            if (val is not null && !col.Validate(val))
                throw new TypeMismatchException(col.Name, col.Type, val.GetType());
        }
    }

    private void IndexRow(int rowId, Row row, bool adding)
    {
        if (_pkIndex != null && Schema.PrimaryKey != null)
        {
            if (row.TryGetValue(Schema.PrimaryKey.Name, out var pkVal) && pkVal is IComparable cmp)
            {
                if (adding) _pkIndex.Insert(cmp, rowId);
                else        _pkIndex.Remove(cmp, rowId);
            }
        }
        foreach (var (colName, idx) in _uniqueIndexes)
        {
            if (row.TryGetValue(colName, out var uVal) && uVal != null)
            {
                if (adding) idx.Insert(uVal, rowId);
                else        idx.Remove(uVal, rowId);
            }
        }
    }

    private void Publish(DbEventKind kind, string detail) =>
        EventBus.Instance.Publish(new DbEvent
        {
            Kind      = kind,
            TableName = Schema.TableName,
            Detail    = detail
        });

    public void Dispose() => _rwl.Dispose();
}
