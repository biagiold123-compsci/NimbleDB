using System.Diagnostics;
using System.Text.RegularExpressions;
using NimbleDB.Core;
using NimbleDB.Exceptions;
using NimbleDB.Observability;
using NimbleDB.Storage;

namespace NimbleDB.Query;

public sealed class ResultSet
{
    public IReadOnlyList<string> Columns { get; init; } = [];
    public IReadOnlyList<Row> Rows { get; init; } = [];
    public long ElapsedMs { get; init; }
    public string? Message { get; init; }

    public static ResultSet Affected(int n, long ms) =>
        new() { Message = $"{n} row(s) affected", ElapsedMs = ms };

    public void Print()
    {
        if (Message is { } msg) Console.WriteLine($"  {msg}  ({ElapsedMs}ms)");
        if (Rows.Count == 0) return;

        var widths = Columns.Select(c => c.Length).ToArray();
        for (int r = 0; r < Rows.Count; r++)
            for (int c = 0; c < Columns.Count; c++)
                widths[c] = Math.Max(widths[c], (Rows[r][Columns[c]]?.ToString() ?? "NULL").Length);

        string Sep() => "  +" + string.Join("+", widths.Select(w => new string('-', w + 2))) + "+";
        string Line(IEnumerable<string> cells) =>
            "  |" + string.Join("|", cells.Select((v, i) => $" {v.PadRight(widths[i])} ")) + "|";

        Console.WriteLine(Sep());
        Console.WriteLine(Line(Columns));
        Console.WriteLine(Sep());
        foreach (var row in Rows)
            Console.WriteLine(Line(Columns.Select(c => row[c]?.ToString() ?? "NULL")));
        Console.WriteLine(Sep());
        Console.WriteLine($"  {Rows.Count} row(s)  ({ElapsedMs}ms)");
    }
}

public sealed class QueryExecutor
{
    private readonly Dictionary<string, TableEngine> _tables;
    public QueryExecutor(Dictionary<string, TableEngine> tables) => _tables = tables;

    public ResultSet Execute(Statement stmt)
    {
        var sw = Stopwatch.StartNew();
        var result = stmt switch
        {
            SelectStatement s => ExecSelect(s),
            InsertStatement s => ExecInsert(s),
            DeleteStatement s => ExecDelete(s),
            UpdateStatement s => ExecUpdate(s),
            _ => throw new QueryExecutionException($"Unsupported statement: {stmt.GetType().Name}")
        };
        sw.Stop();
        EventBus.Instance.Publish(new DbEvent
        {
            Kind = DbEventKind.QueryExecuted,
            TableName = TableName(stmt),
            ElapsedMs = sw.ElapsedMilliseconds
        });
        return result;
    }

    private ResultSet ExecSelect(SelectStatement s)
    {
        var sw = Stopwatch.StartNew();
        var table = Get(s.Table);
        Func<Row, bool> pred = s.Where is null ? _ => true : r => EvalBool(s.Where, r);
        IEnumerable<Row> rows = table.Scan(pred).Select(t => t.Row);

        foreach (var (col, asc) in Enumerable.Reverse(s.OrderBy))
            rows = asc ? rows.OrderBy(r => r[col] as IComparable)
                       : rows.OrderByDescending(r => r[col] as IComparable);

        if (s.Limit.HasValue) rows = rows.Take(s.Limit.Value);

        var allRows = rows.ToList();
        var cols = s.Columns.Count == 0
            ? table.Schema.Columns.Select(c => c.Name).ToList()
            : s.Columns;

        var projected = allRows
            .Select(r => new Row(cols.ToDictionary(c => c, c => r[c])))
            .ToList();

        sw.Stop();
        return new ResultSet { Columns = cols, Rows = projected, ElapsedMs = sw.ElapsedMilliseconds };
    }

    private ResultSet ExecInsert(InsertStatement s)
    {
        var sw = Stopwatch.StartNew();
        var table = Get(s.Table);
        var rows = s.Rows.Select(exprs =>
        {
            if (exprs.Count != s.Columns.Count)
                throw new QueryExecutionException($"Column/value count mismatch.");
            var dict = s.Columns
                .Zip(exprs, (c, e) => (c, v: EvalLit(e)))
                .ToDictionary(t => t.c, t => t.v);
            foreach (var col in table.Schema.Columns)
            {
                if (!dict.ContainsKey(col.Name))
                    dict[col.Name] = col.Default;
                else if (col.Type == Core.DataType.Timestamp && dict[col.Name] is string ts)
                    dict[col.Name] = DateTime.Parse(ts, null, System.Globalization.DateTimeStyles.RoundtripKind);
            }
            return new Row(dict);
        }).ToList();
        int count = table.BulkInsert(rows);
        sw.Stop();
        return ResultSet.Affected(count, sw.ElapsedMilliseconds);
    }

    private ResultSet ExecDelete(DeleteStatement s)
    {
        var sw = Stopwatch.StartNew();
        var table = Get(s.Table);
        int n = table.Delete(s.Where is null ? _ => true : r => EvalBool(s.Where, r));
        sw.Stop();
        return ResultSet.Affected(n, sw.ElapsedMilliseconds);
    }

    private ResultSet ExecUpdate(UpdateStatement s)
    {
        var sw = Stopwatch.StartNew();
        var table = Get(s.Table);
        int n = table.Update(
            s.Where is null ? _ => true : r => EvalBool(s.Where, r),
            row =>
            {
                var d = new Dictionary<string, object?>(row.Data, StringComparer.OrdinalIgnoreCase);
                foreach (var (col, expr) in s.Assignments) d[col] = EvalLit(expr);
                return new Row(d);
            });
        sw.Stop();
        return ResultSet.Affected(n, sw.ElapsedMilliseconds);
    }

    // ── Expression Evaluator ──────────────────────────────────────────────────
    private bool EvalBool(Expr e, Row r) => EvalExpr(e, r) is true;

    private object? EvalExpr(Expr expr, Row row) => expr switch
    {
        Literal l            => l.Value,
        ColumnRef r          => row[r.Column],
        IsNullExpr n         => (EvalExpr(n.Operand, row) is null) ^ n.IsNot,
        LikeExpr lk          => EvalLike(lk, row),
        InExpr inv           => EvalIn(inv, row),
        UnaryOp { Op: "NOT" } u => !(bool)EvalExpr(u.Operand, row)!,
        BinaryOp b           => EvalBin(b, row),
        _                    => throw new QueryExecutionException($"Unknown expression: {expr.GetType().Name}")
    };

    private object? EvalLike(LikeExpr expr, Row row)
    {
        var val = EvalExpr(expr.Operand, row)?.ToString();
        if (val is null) return false;
        var pattern = "^" + Regex.Escape(expr.Pattern).Replace("%", ".*").Replace("_", ".") + "$";
        return Regex.IsMatch(val, pattern, RegexOptions.IgnoreCase);
    }

    private object? EvalIn(InExpr expr, Row row)
    {
        var val = EvalExpr(expr.Operand, row);
        bool found = expr.Values.Any(v => Equals(EvalExpr(v, row), val));
        return expr.IsNot ? !found : found;
    }

    private object? EvalBin(BinaryOp b, Row row)
    {
        if (b.Op == "AND") return EvalBool(b.Left, row) && EvalBool(b.Right, row);
        if (b.Op == "OR")  return EvalBool(b.Left, row) || EvalBool(b.Right, row);
        var left = EvalExpr(b.Left, row);
        var right = EvalExpr(b.Right, row);
        if (left is long li && right is double) left = (double)li;
        if (right is long ri && left is double) right = (double)ri;
        return b.Op switch
        {
            "="  => Equals(left, right),
            "<>" => !Equals(left, right),
            "<"  => Cmp(left, right) < 0,
            "<=" => Cmp(left, right) <= 0,
            ">"  => Cmp(left, right) > 0,
            ">=" => Cmp(left, right) >= 0,
            _    => throw new QueryExecutionException($"Unknown operator: {b.Op}")
        };
    }

    private static int Cmp(object? a, object? b) =>
        a is IComparable ca && b is not null ? ca.CompareTo(b) : 0;

    private static object? EvalLit(Expr e) =>
        e is Literal l ? l.Value : throw new QueryExecutionException("Expected literal.");

    private TableEngine Get(string name) =>
        _tables.TryGetValue(name, out var t) ? t : throw new TableNotFoundException(name);

    private static string TableName(Statement s) => s switch
    {
        SelectStatement x => x.Table,
        InsertStatement x => x.Table,
        DeleteStatement x => x.Table,
        UpdateStatement x => x.Table,
        _ => "?"
    };
}
