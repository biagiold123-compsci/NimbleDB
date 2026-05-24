using NimbleDB.Exceptions;

namespace NimbleDB.Query;

// ── Token Types ───────────────────────────────────────────────────────────────
public enum TokenKind
{
    Select, From, Where, Insert, Into, Values, Delete, Update, Set,
    Create, Table, Drop, Order, By, Asc, Desc, Limit, And, Or, Not,
    Is, Null, Like, In,
    Identifier, IntLiteral, FloatLiteral, StringLiteral, BoolLiteral,
    Eq, NotEq, Lt, Lte, Gt, Gte, Comma, Star, LParen, RParen, Semicolon, Dot,
    Eof
}

public sealed record Token(TokenKind Kind, string Lexeme, int Position);

// ── Lexer ─────────────────────────────────────────────────────────────────────
public sealed class Lexer
{
    private static readonly Dictionary<string, TokenKind> Keywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SELECT"] = TokenKind.Select,  ["FROM"]    = TokenKind.From,
            ["WHERE"]  = TokenKind.Where,   ["INSERT"]  = TokenKind.Insert,
            ["INTO"]   = TokenKind.Into,    ["VALUES"]  = TokenKind.Values,
            ["DELETE"] = TokenKind.Delete,  ["UPDATE"]  = TokenKind.Update,
            ["SET"]    = TokenKind.Set,     ["CREATE"]  = TokenKind.Create,
            ["TABLE"]  = TokenKind.Table,   ["DROP"]    = TokenKind.Drop,
            ["ORDER"]  = TokenKind.Order,   ["BY"]      = TokenKind.By,
            ["ASC"]    = TokenKind.Asc,     ["DESC"]    = TokenKind.Desc,
            ["LIMIT"]  = TokenKind.Limit,   ["AND"]     = TokenKind.And,
            ["OR"]     = TokenKind.Or,      ["NOT"]     = TokenKind.Not,
            ["IS"]     = TokenKind.Is,      ["NULL"]    = TokenKind.Null,
            ["LIKE"]   = TokenKind.Like,    ["IN"]      = TokenKind.In,
            ["TRUE"]   = TokenKind.BoolLiteral, ["FALSE"] = TokenKind.BoolLiteral,
        };

    private readonly string _src;
    private int _pos;

    public Lexer(string source) { _src = source; }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (_pos < _src.Length)
        {
            SkipWhitespace();
            if (_pos >= _src.Length) break;
            char ch = _src[_pos];
            if (char.IsLetter(ch) || ch == '_') tokens.Add(ReadWord());
            else if (char.IsDigit(ch))           tokens.Add(ReadNumber());
            else if (ch == '\'')                 tokens.Add(ReadString());
            else                                 tokens.Add(ReadSymbol());
        }
        tokens.Add(new Token(TokenKind.Eof, "", _pos));
        return tokens;
    }

    private void SkipWhitespace()
    {
        while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos])) _pos++;
    }

    private Token ReadWord()
    {
        int start = _pos;
        while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_')) _pos++;
        var lex = _src[start.._pos];
        var kind = Keywords.TryGetValue(lex, out var kw) ? kw : TokenKind.Identifier;
        return new Token(kind, lex, start);
    }

    private Token ReadNumber()
    {
        int start = _pos;
        while (_pos < _src.Length && char.IsDigit(_src[_pos])) _pos++;
        bool isFloat = _pos < _src.Length && _src[_pos] == '.';
        if (isFloat) { _pos++; while (_pos < _src.Length && char.IsDigit(_src[_pos])) _pos++; }
        return new Token(isFloat ? TokenKind.FloatLiteral : TokenKind.IntLiteral, _src[start.._pos], start);
    }

    private Token ReadString()
    {
        int start = _pos++;
        while (_pos < _src.Length && _src[_pos] != '\'') _pos++;
        _pos++;
        return new Token(TokenKind.StringLiteral, _src[(start + 1)..(_pos - 1)], start);
    }

    private Token ReadSymbol()
    {
        int start = _pos;
        char ch = _src[_pos++];
        return ch switch
        {
            ',' => new Token(TokenKind.Comma,     ",",  start),
            '*' => new Token(TokenKind.Star,      "*",  start),
            '(' => new Token(TokenKind.LParen,    "(",  start),
            ')' => new Token(TokenKind.RParen,    ")",  start),
            ';' => new Token(TokenKind.Semicolon, ";",  start),
            '.' => new Token(TokenKind.Dot,       ".",  start),
            '=' => new Token(TokenKind.Eq,        "=",  start),
            '<' when Peek('=') => Adv(new Token(TokenKind.Lte,   "<=", start)),
            '<' when Peek('>') => Adv(new Token(TokenKind.NotEq, "<>", start)),
            '<' => new Token(TokenKind.Lt,  "<",  start),
            '>' when Peek('=') => Adv(new Token(TokenKind.Gte, ">=", start)),
            '>' => new Token(TokenKind.Gt,  ">",  start),
            '!' when Peek('=') => Adv(new Token(TokenKind.NotEq, "!=", start)),
            _ => throw new QuerySyntaxException(_src, $"Unexpected character '{ch}'", start)
        };
    }

    private bool Peek(char c) => _pos < _src.Length && _src[_pos] == c;
    private Token Adv(Token t) { _pos++; return t; }
}

// ── AST ───────────────────────────────────────────────────────────────────────
public abstract record Expr;
public sealed record ColumnRef(string Table, string Column) : Expr;
public sealed record Literal(object? Value) : Expr;
public sealed record BinaryOp(Expr Left, string Op, Expr Right) : Expr;
public sealed record UnaryOp(string Op, Expr Operand) : Expr;
public sealed record IsNullExpr(Expr Operand, bool IsNot) : Expr;
public sealed record LikeExpr(Expr Operand, string Pattern) : Expr;
public sealed record InExpr(Expr Operand, List<Expr> Values, bool IsNot) : Expr;

public abstract record Statement;
public sealed record SelectStatement(
    List<string> Columns, string Table, Expr? Where,
    List<(string Column, bool Ascending)> OrderBy, int? Limit) : Statement;
public sealed record InsertStatement(
    string Table, List<string> Columns, List<List<Expr>> Rows) : Statement;
public sealed record DeleteStatement(string Table, Expr? Where) : Statement;
public sealed record UpdateStatement(
    string Table, List<(string Column, Expr Value)> Assignments, Expr? Where) : Statement;

// ── Recursive-Descent Parser ──────────────────────────────────────────────────
public sealed class QueryParser
{
    private readonly List<Token> _tokens;
    private int _pos;
    private readonly string _raw;

    public QueryParser(string query)
    {
        _raw = query;
        _tokens = new Lexer(query).Tokenize();
    }

    public Statement Parse()
    {
        Statement stmt = Cur.Kind switch
        {
            TokenKind.Select => (Statement)ParseSelect(),
            TokenKind.Insert => ParseInsert(),
            TokenKind.Delete => ParseDelete(),
            TokenKind.Update => ParseUpdate(),
            _ => throw Err($"Unexpected token '{Cur.Lexeme}'")
        };
        TryEat(TokenKind.Semicolon);
        return stmt;
    }

    private SelectStatement ParseSelect()
    {
        Eat(TokenKind.Select);
        var cols = ParseColList();
        Eat(TokenKind.From);
        var table = EatId();
        Expr? where = TryEat(TokenKind.Where) ? ParseOr() : null;
        var order = new List<(string, bool)>();
        if (TryEat(TokenKind.Order))
        {
            TryEat(TokenKind.By);
            do { var c = EatId(); bool asc = !TryEat(TokenKind.Desc); TryEat(TokenKind.Asc); order.Add((c, asc)); }
            while (TryEat(TokenKind.Comma));
        }
        int? limit = null;
        if (TryEat(TokenKind.Limit)) { limit = int.Parse(Cur.Lexeme); _pos++; }
        return new SelectStatement(cols, table, where, order, limit);
    }

    private InsertStatement ParseInsert()
    {
        Eat(TokenKind.Insert); Eat(TokenKind.Into);
        var table = EatId();
        Eat(TokenKind.LParen);
        var cols = new List<string>(); do { cols.Add(EatId()); } while (TryEat(TokenKind.Comma));
        Eat(TokenKind.RParen); Eat(TokenKind.Values);
        var allRows = new List<List<Expr>>();
        do
        {
            Eat(TokenKind.LParen);
            var vals = new List<Expr>(); do { vals.Add(ParseLit()); } while (TryEat(TokenKind.Comma));
            Eat(TokenKind.RParen); allRows.Add(vals);
        } while (TryEat(TokenKind.Comma));
        return new InsertStatement(table, cols, allRows);
    }

    private DeleteStatement ParseDelete()
    {
        Eat(TokenKind.Delete); Eat(TokenKind.From);
        var table = EatId();
        Expr? where = TryEat(TokenKind.Where) ? ParseOr() : null;
        return new DeleteStatement(table, where);
    }

    private UpdateStatement ParseUpdate()
    {
        Eat(TokenKind.Update);
        var table = EatId(); Eat(TokenKind.Set);
        var sets = new List<(string, Expr)>();
        do { var c = EatId(); Eat(TokenKind.Eq); sets.Add((c, ParseLit())); } while (TryEat(TokenKind.Comma));
        Expr? where = TryEat(TokenKind.Where) ? ParseOr() : null;
        return new UpdateStatement(table, sets, where);
    }

    private Expr ParseOr()
    {
        var l = ParseAnd();
        while (TryEat(TokenKind.Or)) l = new BinaryOp(l, "OR", ParseAnd());
        return l;
    }

    private Expr ParseAnd()
    {
        var l = ParseNot();
        while (TryEat(TokenKind.And)) l = new BinaryOp(l, "AND", ParseNot());
        return l;
    }

    private Expr ParseNot() =>
        TryEat(TokenKind.Not) ? new UnaryOp("NOT", ParseCmp()) : ParseCmp();

    private Expr ParseCmp()
    {
        var left = ParsePrimary();
        if (Cur.Kind == TokenKind.Is)
        {
            Eat(TokenKind.Is); bool isNot = TryEat(TokenKind.Not); Eat(TokenKind.Null);
            return new IsNullExpr(left, isNot);
        }
        if (Cur.Kind == TokenKind.Like)
        {
            Eat(TokenKind.Like); var pat = Cur.Lexeme; Eat(TokenKind.StringLiteral);
            return new LikeExpr(left, pat);
        }
        if (Cur.Kind == TokenKind.In)
        {
            Eat(TokenKind.In); Eat(TokenKind.LParen);
            var vals = new List<Expr>(); do { vals.Add(ParseLit()); } while (TryEat(TokenKind.Comma));
            Eat(TokenKind.RParen);
            return new InExpr(left, vals, false);
        }
        string? op = Cur.Kind switch
        {
            TokenKind.Eq => "=", TokenKind.NotEq => "<>",
            TokenKind.Lt => "<", TokenKind.Lte => "<=",
            TokenKind.Gt => ">", TokenKind.Gte => ">=", _ => null
        };
        if (op != null) { _pos++; return new BinaryOp(left, op, ParsePrimary()); }
        return left;
    }

    private Expr ParsePrimary()
    {
        if (TryEat(TokenKind.LParen)) { var e = ParseOr(); Eat(TokenKind.RParen); return e; }
        if (Cur.Kind == TokenKind.Identifier)
        {
            var name = EatId();
            if (TryEat(TokenKind.Dot)) return new ColumnRef(name, EatId());
            return new ColumnRef("", name);
        }
        return ParseLit();
    }

    private Expr ParseLit()
    {
        return Cur.Kind switch
        {
            TokenKind.IntLiteral    => Next(new Literal(long.Parse(Cur.Lexeme))),
            TokenKind.FloatLiteral  => Next(new Literal(double.Parse(Cur.Lexeme))),
            TokenKind.StringLiteral => Next(new Literal(Cur.Lexeme)),
            TokenKind.BoolLiteral   => Next(new Literal(Cur.Lexeme.Equals("true", StringComparison.OrdinalIgnoreCase))),
            TokenKind.Null          => Next(new Literal(null)),
            _ => throw Err($"Expected literal, got '{Cur.Lexeme}'")
        };
        Expr Next(Expr e) { _pos++; return e; }
    }

    private List<string> ParseColList()
    {
        if (Cur.Kind == TokenKind.Star) { _pos++; return []; }
        var cols = new List<string>(); do { cols.Add(EatId()); } while (TryEat(TokenKind.Comma));
        return cols;
    }

    private Token Cur => _tokens[_pos];
    private Token Eat(TokenKind k) { if (Cur.Kind != k) throw Err($"Expected {k}, got '{Cur.Lexeme}'"); return _tokens[_pos++]; }
    private bool TryEat(TokenKind k) { if (Cur.Kind == k) { _pos++; return true; } return false; }
    private string EatId() { if (Cur.Kind != TokenKind.Identifier) throw Err($"Expected identifier, got '{Cur.Lexeme}'"); return _tokens[_pos++].Lexeme; }
    private QuerySyntaxException Err(string msg) => new(_raw, msg, Cur.Position);
}
