# NimbleDB

A fully functional, in-memory relational database engine built from scratch in **C# 12** and **.NET 8**, demonstrating mastery of systems programming, concurrency, data structures, and software architecture patterns.

---

## Features

| Feature | Detail |
|---|---|
| **Custom Query Language** | Hand-written lexer and recursive-descent parser supporting `SELECT`, `INSERT`, `UPDATE`, `DELETE` |
| **WHERE Clause Evaluation** | Boolean expressions, `AND`/`OR`/`NOT`, `LIKE` pattern matching, `IN`, `IS NULL`, and comparison operators |
| **Thread-Safe Storage** | `ReaderWriterLockSlim` with MVCC-lite snapshot semantics for concurrent reads and exclusive writes |
| **Dual Indexing** | Generic sorted index (Red-Black tree) for range scans and hash index for O(1) equality lookups |
| **JSON Persistence** | Full save/restore of schema and row data via `System.Text.Json` |
| **Observer / Event Bus** | Singleton publish-subscribe system with pluggable console logger and metrics collector |
| **Session Metrics** | Tracks insert, update, delete, and query counts with average query latency |
| **Interactive REPL** | Command-line shell with query history, live event toggle, and database statistics |
| **Bulk DML** | Batch insert of multiple rows in a single validated, indexed write |
| **Custom Exception Hierarchy** | Typed exceptions for constraint violations, syntax errors, and schema mismatches |

---

## Project Structure

```
NimbleDB2/
├── CLI/
│   └── Repl.cs                # Interactive REPL shell and meta-commands
├── Core/
│   └── Schema.cs              # DataType enum, ColumnDefinition, TableSchema, Row
├── Exceptions/
│   └── Exceptions.cs          # Full custom exception hierarchy
├── Indexing/
│   └── Index.cs               # Generic SortedIndex and HashIndex
├── Observability/
│   └── EventBus.cs            # Singleton EventBus, ConsoleLogger, MetricsCollector
├── Query/
│   ├── Parser.cs              # Lexer, AST node types, recursive-descent parser
│   └── Executor.cs            # Query executor and ResultSet with pretty-print
├── Serialization/
│   └── Serializer.cs          # JSON save/load via System.Text.Json
├── Storage/
│   └── TableEngine.cs         # Thread-safe table engine with indexing and validation
├── Database.cs                # High-level database facade (public API)
├── Program.cs                 # Demo: schema, queries, concurrency test, REPL
└── NimbleDB.csproj
```

---

## Key C# Concepts Demonstrated

- **Generics** — `SortedIndex<K>`, `HashIndex<K>` with type constraints
- **Records and positional syntax** — `ColumnDefinition`, all AST node types
- **ReaderWriterLockSlim** — concurrent reads with exclusive writes
- **Volatile reads** — MVCC-lite row slot access without full locks
- **Interlocked** — atomic counters in the metrics collector and stress test
- **ConcurrentDictionary** — thread-safe table catalog
- **LINQ** — projection, filtering, ordering, and aggregation over live table scans
- **IAsyncEnumerable-ready design** — cooperative scan pattern with predicate injection
- **Custom exception hierarchy** — abstract base with sealed derived types
- **Observer pattern** — decoupled event bus with multiple subscriber types
- **Facade pattern** — `Database` class hides engine complexity behind a clean API
- **Singleton pattern** — `EventBus.Instance` with lazy thread-safe initialization
- **System.Text.Json** — schema-aware serialization with type coercion on load
- **Recursive-descent parsing** — hand-written Pratt-style expression parser
- **ICloneable** — deep row cloning for safe `UPDATE` transforms

---

## Getting Started

### Prerequisites

- .NET 8 SDK
- Visual Studio Code with the C# Dev Kit extension, or Visual Studio 2022+

### Run

```bash
cd NimbleDB2
dotnet run
```

The program will run an automated demo covering all features, then drop you into the interactive REPL.

---

## Using the Application

### REPL Queries

```sql
SELECT * FROM users;

SELECT username, score FROM users WHERE score > 7 ORDER BY score DESC LIMIT 5;

INSERT INTO users (id, username, email, age, is_active, score, created_at)
VALUES (99, 'zara', 'zara@example.com', 26, true, 8.5, '2024-09-01T10:00:00Z');

UPDATE users SET is_active = false WHERE score < 5;

DELETE FROM users WHERE is_active = false;
```

### Meta Commands

| Command | Description |
|---|---|
| `.tables` | List all tables |
| `.stats` | Show row counts and column counts per table |
| `.log` | Toggle live event logging on/off |
| `.save <file>` | Save the database to a JSON file |
| `.load <file>` | Load a database from a JSON file |
| `.history` | Print query history for this session |
| `.help` | Print command reference |
| `.quit` | Exit the program |

### WHERE Clause Operators

| Operator | Example |
|---|---|
| **Comparison** | `age > 25`, `score <= 9.5`, `id <> 3` |
| **Equality** | `username = 'alice'` |
| **LIKE** | `username LIKE '%ali%'` |
| **IN** | `category IN ('Electronics', 'Furniture')` |
| **IS NULL / IS NOT NULL** | `email IS NOT NULL` |
| **AND / OR / NOT** | `is_active = true AND score > 7` |

---

## Thread-Safety Contract

The table engine supports many concurrent readers and one writer at a time via `ReaderWriterLockSlim`. The MVCC-lite design takes a pointer snapshot of row slots before filtering, meaning reads never block on in-progress writes. The built-in stress test runs 8 concurrent writer threads and 4 concurrent reader threads simultaneously with zero data races.
