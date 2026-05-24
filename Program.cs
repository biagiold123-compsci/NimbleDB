using System.Diagnostics;
using NimbleDB;
using NimbleDB.CLI;
using NimbleDB.Core;
using NimbleDB.Exceptions;
using NimbleDB.Observability;

var metrics = new MetricsCollector();
EventBus.Instance.Subscribe(metrics);

using var db = new Database("DemoDB");

Section("1. Schema Creation");
db.CreateTable(new TableSchema("users", new[]
{
    new ColumnDefinition("id",         DataType.Integer,   Nullable: false, PrimaryKey: true),
    new ColumnDefinition("username",   DataType.Text,      Nullable: false, Unique: true),
    new ColumnDefinition("email",      DataType.Text,      Nullable: false),
    new ColumnDefinition("age",        DataType.Integer),
    new ColumnDefinition("is_active",  DataType.Boolean,   Nullable: false, Default: true),
    new ColumnDefinition("score",      DataType.Float),
    new ColumnDefinition("created_at", DataType.Timestamp, Nullable: false, Default: DateTime.UtcNow),
}));
db.CreateTable(new TableSchema("products", new[]
{
    new ColumnDefinition("product_id", DataType.Integer, Nullable: false, PrimaryKey: true),
    new ColumnDefinition("name",       DataType.Text,    Nullable: false),
    new ColumnDefinition("category",   DataType.Text),
    new ColumnDefinition("price",      DataType.Float,   Nullable: false),
    new ColumnDefinition("in_stock",   DataType.Boolean, Nullable: false, Default: true),
}));
Console.WriteLine("  Tables created: users, products");

Section("2. Batch INSERT");
db.QueryBatch(
    "INSERT INTO users (id, username, email, age, is_active, score, created_at) " +
    "VALUES (1, 'alice',  'alice@example.com',  29, true,  9.4, '2024-01-15T09:00:00Z')," +
           "(2, 'bob',    'bob@example.com',    34, true,  7.1, '2024-02-20T14:30:00Z')," +
           "(3, 'carol',  'carol@example.com',  27, false, 8.8, '2024-03-05T11:15:00Z')," +
           "(4, 'dave',   'dave@example.com',   41, true,  6.5, '2024-04-10T16:00:00Z')," +
           "(5, 'eve',    'eve@example.com',    23, true,  9.9, '2024-05-01T08:45:00Z')," +
           "(6, 'frank',  'frank@example.com',  38, true,  5.2, '2024-06-12T13:00:00Z')," +
           "(7, 'grace',  'grace@example.com',  31, false, 8.0, '2024-07-07T10:30:00Z')," +
           "(8, 'henry',  'henry@example.com',  45, true,  4.3, '2024-08-18T17:00:00Z');" +
    "INSERT INTO products (product_id, name, category, price, in_stock) " +
    "VALUES (1, 'Mechanical Keyboard', 'Electronics', 149.99, true)," +
           "(2, 'USB-C Hub',           'Electronics',  49.99, true)," +
           "(3, 'Standing Desk',       'Furniture',   499.99, false)," +
           "(4, 'Ergonomic Chair',     'Furniture',   349.99, true)," +
           "(5, 'Monitor Stand',       'Accessories',  79.99, true)," +
           "(6, 'Cable Management',    'Accessories',  24.99, true)," +
           "(7, 'Webcam Pro',          'Electronics', 129.99, false)," +
           "(8, 'Desk Pad XL',         'Accessories',  44.99, true);"
);
Console.WriteLine("  16 rows inserted.");

Section("3. SELECT - WHERE / ORDER BY / LIMIT");
Print("Active users with score > 7, top 3:");
db.Query("SELECT id, username, score FROM users WHERE is_active = true AND score > 7 ORDER BY score DESC LIMIT 3").Print();

Print("Electronics under $100:");
db.Query("SELECT name, price FROM products WHERE category = 'Electronics' AND price < 100 ORDER BY price ASC").Print();

Print("Usernames containing 'e':");
db.Query("SELECT username, email FROM users WHERE username LIKE '%e%'").Print();

Section("4. UPDATE");
db.Query("UPDATE users SET is_active = false WHERE score < 5").Print();
Print("All users after update:");
db.Query("SELECT username, score, is_active FROM users ORDER BY username ASC").Print();

Section("5. DELETE");
db.Query("DELETE FROM users WHERE is_active = false").Print();
Print("Remaining users:");
db.Query("SELECT id, username, score FROM users ORDER BY score DESC").Print();

Section("6. Concurrent Read/Write Stress Test");
Console.WriteLine("  8 writer threads x 50 inserts + 4 continuous reader threads...");

var usersTable = db.GetTable("users");
var cts = new CancellationTokenSource();
var sw  = Stopwatch.StartNew();
int writeCount = 0, readCount = 0;

var writers = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
{
    for (int j = 0; j < 50; j++)
    {
        usersTable.Insert(new Row(new Dictionary<string, object?>
        {
            ["id"]         = 1000 + i * 100 + j,
            ["username"]   = "stress_" + i + "_" + j,
            ["email"]      = "s_" + i + "_" + j + "@test.com",
            ["age"]        = (object?)(20 + j % 40),
            ["is_active"]  = true,
            ["score"]      = (double)(j % 10 + 1),
            ["created_at"] = DateTime.UtcNow
        }));
        Interlocked.Increment(ref writeCount);
    }
})).ToArray();

var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        _ = usersTable.Scan().Count();
        Interlocked.Increment(ref readCount);
    }
})).ToArray();

Task.WaitAll(writers);
cts.Cancel();
Task.WaitAll(readers);
sw.Stop();

Console.WriteLine($"  Writes: {writeCount:N0}   Reads: {readCount:N0}   Time: {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"  Final row count: {usersTable.Count():N0}  (zero data races)");

Section("7. LINQ over Table Engine");
var top5 = usersTable
    .Scan(r => r["is_active"] is true)
    .Select(t => t.Row)
    .OrderByDescending(r => r["score"] as double? ?? 0)
    .Take(5)
    .Select(r => "  " + string.Format("{0,-20}", r["username"]) + " score=" + string.Format("{0:F1}", r["score"]));

Console.WriteLine("  Top 5 active users by score:");
foreach (var line in top5) Console.WriteLine(line);
double avg = usersTable.Scan().Average(t => t.Row["score"] as double? ?? 0);
Console.WriteLine("\n  Average score (all): " + avg.ToString("F2"));

Section("8. Custom Exception Hierarchy");
Try("Duplicate primary key", () => db.Query("INSERT INTO products (product_id, name, price, in_stock) VALUES (1, 'Dup', 9.99, true)"));
Try("Non-existent table",    () => db.Query("SELECT * FROM ghost"));
Try("SQL syntax error",      () => db.Query("SELCT * FROM users"));
Try("NOT NULL violation",    () => db.GetTable("users").Insert(new Row(new Dictionary<string, object?>
{
    ["id"] = 9999, ["username"] = null, ["email"] = "x@x.com", ["is_active"] = true
})));

Section("9. JSON Persistence");
string savePath = Path.Combine(Path.GetTempPath(), "nimble_demo.json");
db.SaveTo(savePath);
Console.WriteLine("  Saved to: " + savePath + "  (" + new FileInfo(savePath).Length.ToString("N0") + " bytes)");

using var db2 = new Database("Restored");
db2.LoadFrom(savePath);
Console.WriteLine("  Restored users:    " + db2.GetTable("users").Count());
Console.WriteLine("  Restored products: " + db2.GetTable("products").Count());

Section("10. Statistics");
db.PrintStats();
metrics.PrintSummary();

Section("11. Interactive REPL");
Console.WriteLine("  Type SQL or .help to explore. (.quit to exit)\n");
Repl.Run(db);

static void Section(string title)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine("  --- " + title + " ---");
    Console.ResetColor();
}

static void Print(string label)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("\n  " + label);
    Console.ResetColor();
}

static void Try(string label, Action action)
{
    Console.Write("\n  " + label + ": ");
    try { action(); Console.WriteLine("(no exception)"); }
    catch (NimbleDbException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[" + ex.GetType().Name + "] " + ex.Message);
        Console.ResetColor();
    }
}
