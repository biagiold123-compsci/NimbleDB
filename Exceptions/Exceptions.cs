using NimbleDB.Core;

namespace NimbleDB.Exceptions;

public abstract class NimbleDbException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class TableNotFoundException(string tableName)
    : NimbleDbException($"Table '{tableName}' does not exist.");

public sealed class TableAlreadyExistsException(string tableName)
    : NimbleDbException($"Table '{tableName}' already exists.");

public sealed class ColumnNotFoundException(string tableName, string columnName)
    : NimbleDbException($"Column '{columnName}' not found in table '{tableName}'.");

public sealed class PrimaryKeyViolationException(string tableName, object key)
    : NimbleDbException($"Primary key '{key}' already exists in table '{tableName}'.");

public sealed class UniqueConstraintViolationException(string tableName, string column, object? value)
    : NimbleDbException($"Unique constraint violated on '{tableName}.{column}' for value '{value}'.");

public sealed class NullConstraintViolationException(string tableName, string column)
    : NimbleDbException($"NOT NULL constraint violated on '{tableName}.{column}'.");

public sealed class TypeMismatchException(string column, DataType expected, Type actual)
    : NimbleDbException($"Type mismatch on column '{column}': expected {expected}, got {actual.Name}.");

public sealed class QuerySyntaxException(string query, string reason, int position = -1)
    : NimbleDbException(position >= 0
        ? $"Syntax error at position {position} in query '{query}': {reason}"
        : $"Syntax error in query '{query}': {reason}");

public sealed class QueryExecutionException(string message, Exception? inner = null)
    : NimbleDbException(message, inner);
