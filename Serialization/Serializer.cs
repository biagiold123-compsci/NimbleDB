using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NimbleDB.Core;
using NimbleDB.Storage;

namespace NimbleDB.Serialization;

public static class DatabaseSerializer
{
    private sealed record SerializedDb(List<SerializedTable> Tables);
    private sealed record SerializedTable(
        string Name,
        List<SerializedColumn> Columns,
        List<Dictionary<string, JsonElement>> Rows);
    private sealed record SerializedColumn(
        string Name, string Type, bool Nullable, bool PrimaryKey, bool Unique);

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Save(string path, IReadOnlyDictionary<string, TableEngine> tables)
    {
        var db = new SerializedDb(tables.Values.Select(SerializeTable).ToList());
        File.WriteAllText(path, JsonSerializer.Serialize(db, Opts), Encoding.UTF8);
    }

    public static Dictionary<string, TableEngine> Load(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        var db = JsonSerializer.Deserialize<SerializedDb>(json, Opts)
                 ?? throw new InvalidDataException("Corrupt database file.");
        var tables = new Dictionary<string, TableEngine>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in db.Tables)
        {
            var cols = t.Columns.Select(c => new ColumnDefinition(
                c.Name, Enum.Parse<DataType>(c.Type),
                c.Nullable, null, c.PrimaryKey, c.Unique)).ToList();
            var schema = new TableSchema(t.Name, cols);
            var engine = new TableEngine(schema);
            var rows = t.Rows.Select(dict => new Row(
                dict.ToDictionary(kv => kv.Key,
                    kv => DeserializeValue(kv.Value,
                        cols.Find(c => c.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))?.Type))));
            engine.BulkInsert(rows);
            tables[t.Name] = engine;
        }
        return tables;
    }

    private static SerializedTable SerializeTable(TableEngine engine)
    {
        var cols = engine.Schema.Columns.Select(c =>
            new SerializedColumn(c.Name, c.Type.ToString(), c.Nullable, c.PrimaryKey, c.Unique)).ToList();
        var rows = engine.Scan().Select(t =>
            t.Row.Data.ToDictionary(kv => kv.Key,
                kv => JsonSerializer.SerializeToElement(kv.Value))).ToList();
        return new SerializedTable(engine.Schema.TableName, cols, rows);
    }

    private static object? DeserializeValue(JsonElement el, DataType? type)
    {
        if (el.ValueKind == JsonValueKind.Null) return null;
        return type switch
        {
            DataType.Integer   => el.GetInt64(),
            DataType.Float     => el.GetDouble(),
            DataType.Boolean   => el.GetBoolean(),
            DataType.Timestamp => el.GetDateTime(),
            _                  => el.GetString()
        };
    }
}
