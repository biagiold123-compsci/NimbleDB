using System.Text;

namespace NimbleDB.Core;

public enum DataType { Integer, Float, Text, Boolean, Timestamp }

public sealed record ColumnDefinition(
    string Name,
    DataType Type,
    bool Nullable = true,
    object? Default = null,
    bool PrimaryKey = false,
    bool Unique = false)
{
    public bool Validate(object? value)
    {
        if (value is null) return Nullable;
        return Type switch
        {
            DataType.Integer   => value is int or long,
            DataType.Float     => value is double or float,
            DataType.Text      => value is string,
            DataType.Boolean   => value is bool,
            DataType.Timestamp => value is DateTime,
            _                  => false
        };
    }
    public override string ToString() =>
        $"{Name} {Type}{(PrimaryKey ? " PK" : "")}{(Nullable ? "" : " NOT NULL")}";
}

public sealed class TableSchema
{
    public string TableName { get; }
    public IReadOnlyList<ColumnDefinition> Columns { get; }
    public ColumnDefinition? PrimaryKey { get; }
    private readonly Dictionary<string, ColumnDefinition> _columnIndex;

    public TableSchema(string tableName, IEnumerable<ColumnDefinition> columns)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name cannot be empty.", nameof(tableName));
        TableName = tableName;
        var cols = columns.ToList();
        Columns = cols.AsReadOnly();
        _columnIndex = cols.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        PrimaryKey = cols.FirstOrDefault(c => c.PrimaryKey);
    }

    public bool HasColumn(string name) => _columnIndex.ContainsKey(name);
    public ColumnDefinition? GetColumn(string name) =>
        _columnIndex.TryGetValue(name, out var col) ? col : null;

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TABLE {TableName} (");
        foreach (var col in Columns) sb.AppendLine($"  {col}");
        sb.Append(')');
        return sb.ToString();
    }
}

public sealed class Row : ICloneable
{
    private readonly Dictionary<string, object?> _data;
    public IReadOnlyDictionary<string, object?> Data => _data;

    public Row(IDictionary<string, object?> data)
    {
        _data = new Dictionary<string, object?>(data, StringComparer.OrdinalIgnoreCase);
    }

    public object? this[string column] =>
        _data.TryGetValue(column, out var val) ? val : null;

    public bool TryGetValue(string column, out object? value) =>
        _data.TryGetValue(column, out value);

    public object Clone() => new Row(new Dictionary<string, object?>(_data));

    public override string ToString()
    {
        var pairs = _data.Select(kv => $"{kv.Key}: {kv.Value ?? "NULL"}");
        return $"{{ {string.Join(", ", pairs)} }}";
    }
}
