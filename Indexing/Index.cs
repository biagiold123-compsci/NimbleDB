namespace NimbleDB.Indexing;

/// <summary>
/// Sorted index backed by a Red-Black tree (SortedDictionary).
/// Supports O(log n) point lookups and O(log n + k) range scans.
/// </summary>
public sealed class SortedIndex<TKey> where TKey : IComparable<TKey>
{
    private readonly SortedDictionary<TKey, HashSet<int>> _tree = new();
    private readonly object _lock = new();

    public string ColumnName { get; }
    public bool Unique { get; }

    public SortedIndex(string columnName, bool unique = false)
    {
        ColumnName = columnName;
        Unique = unique;
    }

    public void Insert(TKey key, int rowId)
    {
        lock (_lock)
        {
            if (!_tree.TryGetValue(key, out var set))
            {
                set = [];
                _tree[key] = set;
            }
            if (Unique && set.Count > 0)
                throw new InvalidOperationException(
                    $"Unique index violation on '{ColumnName}' for key '{key}'.");
            set.Add(rowId);
        }
    }

    public void Remove(TKey key, int rowId)
    {
        lock (_lock)
        {
            if (_tree.TryGetValue(key, out var set))
            {
                set.Remove(rowId);
                if (set.Count == 0) _tree.Remove(key);
            }
        }
    }

    public IReadOnlySet<int> Lookup(TKey key)
    {
        lock (_lock)
            return _tree.TryGetValue(key, out var set) ? set : (IReadOnlySet<int>)new HashSet<int>();
    }

    public IEnumerable<int> RangeScan(TKey? lo, TKey? hi)
    {
        lock (_lock)
        {
            foreach (var (key, rowIds) in _tree)
            {
                if (lo != null && key.CompareTo(lo) < 0) continue;
                if (hi != null && key.CompareTo(hi) > 0) break;
                foreach (var id in rowIds) yield return id;
            }
        }
    }

    public int KeyCount { get { lock (_lock) return _tree.Count; } }
}

/// <summary>
/// Hash index for O(1) average equality lookups.
/// </summary>
public sealed class HashIndex<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, HashSet<int>> _map = new();
    private readonly object _lock = new();

    public string ColumnName { get; }
    public HashIndex(string columnName) => ColumnName = columnName;

    public void Insert(TKey key, int rowId)
    {
        lock (_lock)
        {
            if (!_map.TryGetValue(key, out var set))
            {
                set = [];
                _map[key] = set;
            }
            set.Add(rowId);
        }
    }

    public void Remove(TKey key, int rowId)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var set))
            {
                set.Remove(rowId);
                if (set.Count == 0) _map.Remove(key);
            }
        }
    }

    public IReadOnlySet<int> Lookup(TKey key)
    {
        lock (_lock)
            return _map.TryGetValue(key, out var set) ? set : (IReadOnlySet<int>)new HashSet<int>();
    }
}
