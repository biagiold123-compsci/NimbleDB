NimbleDB
A fully functional, in-memory relational database engine built from scratch in C# 12 / .NET 8, demonstrating mastery of systems programming, concurrency, data structures, and software architecture patterns.

Features
FeatureDetailCustom Query LanguageHand-written lexer and recursive-descent parser supporting SELECT, INSERT, UPDATE, DELETEWHERE Clause EvaluationBoolean expressions, AND/OR/NOT, LIKE pattern matching, IN, IS NULL, comparison operatorsThread-Safe StorageReaderWriterLockSlim with MVCC-lite snapshot semantics for concurrent reads and exclusive writesDual IndexingGeneric sorted index (Red-Black tree) for range scans and hash index for O(1) equality lookupsJSON PersistenceFull save/restore of schema and row data via System.Text.JsonObserver / Event BusSingleton publish-subscribe system with pluggable console logger and metrics collectorSession MetricsTracks insert, update, delete, and query counts with average query latencyInteractive REPLCommand-line shell with query history, live event toggle, and database statisticsBulk DMLBatch insert of multiple rows in a single validated, indexed writeCustom Exception HierarchyTyped exceptions for constraint violations, syntax errors, and schema mismatches

Project Structure
NimbleDB2/
├── CLI/
│   └── Repl.cs                  # Interactive REPL shell and meta-commands
├── Core/
│   └── Schema.cs                # DataType enum, ColumnDefinition, TableSchema, Row
├── Exceptions/
│   └── Exceptions.cs            # Full custom exception hierarchy
├── Indexing/
│   └── Index.cs                 # Generic SortedIndex<T> and HashIndex<T>
├── Observability/
│   └── EventBus.cs              # Singleton EventBus, ConsoleLogger, MetricsCollector
├── Query/
│   ├── Parser.cs                # Lexer, AST node types, recursive-descent parser
│   └── Executor.cs              # Query executor and ResultSet with pretty-print
├── Serialization/
│   └── Serializer.cs            # JSON save/load via System.Text.Json
├── Storage/
│   └── TableEngine.cs           # Thread-safe table engine with indexing and validation
├── Database.cs                  # High-level database facade (public API)
├── Program.cs                   # Demo: schema, queries, concurrency test, REPL
└── NimbleDB.csproj

Key C# Concepts Demonstrated

Generics — SortedIndex<TKey>, HashIndex<TKey> with type constraints
Records and positional syntax — ColumnDefinition, all AST node types
ReaderWriterLockSlim — concurrent reads with exclusive writes
Volatile reads — MVCC-lite row slot access without full locks
Interlocked — atomic counters in the metrics collector and stress test
ConcurrentDictionary — thread-safe table catalog
LINQ — projection, filtering, ordering, and aggregation over live table scans
IAsyncEnumerable-ready design — cooperative scan pattern with predicate injection
Custom exception hierarchy — abstract base with sealed derived types
Observer pattern — decoupled event bus with multiple subscriber types
Facade pattern — Database class hides engine complexity behind a clean API
Singleton pattern — EventBus.Instance with lazy thread-safe initialization
System.Text.Json — schema-aware serialization with type coercion on load
Recursive-descent parsing — hand-written Pratt-style expression parser
ICloneable — deep row cloning for safe UPDATE transforms


Getting Started
Prerequisites

.NET 8 SDK
Visual Studio Code with the C# Dev Kit extension, or Visual Studio 2022+

Run
bashcd NimbleDB2
dotnet run
The program will run an automated demo covering all features, then drop you into the interactive REPL.

Using the REPL
Once the demo completes, you can interact with the live database:
SELECT * FROM users;
SELECT username, score FROM users WHERE score > 7 ORDER BY score DESC LIMIT 5;
INSERT INTO users (id, username, email, age, is_active, score, created_at) VALUES (99, 'zara', 'zara@example.com', 26, true, 8.5, '2024-09-01T10:00:00Z');
UPDATE users SET is_active = false WHERE score < 5;
DELETE FROM users WHERE is_active = false;
Meta Commands
CommandDescription.tablesList all tables.statsShow row counts and column counts per table.logToggle live event logging on/off.save <path>Save the database to a JSON file.load <path>Load a database from a JSON file.historyPrint query history for this session.helpPrint command reference.quitExit the program

Supported SQL Syntax
sql-- SELECT
SELECT * FROM table;
SELECT col1, col2 FROM table WHERE condition ORDER BY col ASC|DESC LIMIT n;

-- INSERT (supports multi-row)
INSERT INTO table (col1, col2) VALUES (val1, val2), (val3, val4);

-- UPDATE
UPDATE table SET col = value WHERE condition;

-- DELETE
DELETE FROM table WHERE condition;
WHERE Clause Operators
OperatorExampleComparisonage > 25, score <= 9.5, id <> 3Equalityusername = 'alice'LIKEusername LIKE '%ali%'INcategory IN ('Electronics', 'Furniture')IS NULL / IS NOT NULLemail IS NOT NULLAND / OR / NOTis_active = true AND score > 7

Thread-Safety Contract
The table engine supports many concurrent readers and one writer at a time via ReaderWriterLockSlim. The MVCC-lite design takes a pointer snapshot of row slots before filtering, meaning reads never block on in-progress writes.
For the stress test built into the demo, 8 concurrent writer threads and 4 concurrent reader threads operate simultaneously with zero data races.

License
MIT — free to use, modify, and distribute.
