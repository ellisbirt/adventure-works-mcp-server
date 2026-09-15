using System.Data;
using System.Data.Common;
using System.Text.Json;
using EnterpriseAiGateway.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using EnterpriseAiGateway.Data.Scaffolded;

namespace EnterpriseAiGateway.Data.Repositories;

public record SafeTableDefinition(string Schema, string Name, IReadOnlyList<string> Columns);

public interface ISecureTableCatalogRepository
{
    Task<IReadOnlyList<SafeTableDefinition>> GetTablesAsync();

    /// <summary>
    /// Reads up to <paramref name="limit"/> rows from the allow-listed columns of a catalog table,
    /// optionally narrowed to rows where <paramref name="filterColumn"/> equals <paramref name="filterValue"/>.
    /// The filter column must itself be part of the safe catalog for the table.
    /// </summary>
    Task<string> GetTableRowsAsync(string schema, string table, int limit, string? filterColumn = null, object? filterValue = null);
}

public sealed class SecureTableCatalogRepository : ISecureTableCatalogRepository
{
    internal const string RedactedMarker = "[REDACTED]";

    private readonly AdventureWorksDbContext _context;

    public SecureTableCatalogRepository(AdventureWorksDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<SafeTableDefinition>> GetTablesAsync()
    {
        // GetDbConnection() returns the DbContext's own shared connection, so it is opened
        // (not disposed) here: disposing it would invalidate it for later calls in the same scope.
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.columns c ON c.object_id = t.object_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name, c.column_id;
            """;

        var tables = new Dictionary<(string Schema, string Name), List<string>>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            (string Schema, string Name) key = (reader.GetString(0), reader.GetString(1));
            if (!tables.TryGetValue(key, out var columns))
            {
                columns = [];
                tables.Add(key, columns);
            }

            var column = reader.GetString(2);
            if (IsAllowedColumn(_context.Model, key.Schema, key.Name, column)) columns.Add(column);
        }

        return tables
            .Where(table => table.Value.Count > 0)
            .Select(table => new SafeTableDefinition(table.Key.Schema, table.Key.Name, table.Value))
            .ToList();
    }

    public async Task<string> GetTableRowsAsync(string schema, string table, int limit, string? filterColumn = null, object? filterValue = null)
    {
        var tableDefinition = (await GetTablesAsync()).FirstOrDefault(candidate =>
            candidate.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase) &&
            candidate.Name.Equals(table, StringComparison.OrdinalIgnoreCase));

        if (tableDefinition is null)
            return "Error: The requested table is unavailable or contains no useful columns.";

        string? matchedFilterColumn = null;
        if (filterColumn is not null)
        {
            matchedFilterColumn = tableDefinition.Columns.FirstOrDefault(column => column.Equals(filterColumn, StringComparison.OrdinalIgnoreCase));
            if (matchedFilterColumn is null)
                return "Error: The requested filter column is unavailable or not part of the safe MCP catalog.";
        }

        // PII columns are never selected from the database at all; they are stamped with the
        // redacted marker after the row is materialized instead of being fetched and discarded.
        var queryColumns = tableDefinition.Columns.Where(column => !IsPiiColumn(_context.Model, schema, table, column)).ToList();
        var piiColumns = tableDefinition.Columns.Where(column => IsPiiColumn(_context.Model, schema, table, column)).ToList();

        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var whereClause = matchedFilterColumn is null ? string.Empty : $" WHERE {QuoteIdentifier(matchedFilterColumn)} = @filterValue";
        command.CommandText = $"SELECT TOP (@limit) {string.Join(", ", queryColumns.Select(QuoteIdentifier))} FROM {QuoteIdentifier(tableDefinition.Schema)}.{QuoteIdentifier(tableDefinition.Name)}{whereClause};";
        var limitParameter = command.CreateParameter();
        limitParameter.ParameterName = "@limit";
        limitParameter.DbType = DbType.Int32;
        limitParameter.Value = limit;
        command.Parameters.Add(limitParameter);

        if (matchedFilterColumn is not null)
        {
            var filterParameter = command.CreateParameter();
            filterParameter.ParameterName = "@filterValue";
            filterParameter.Value = filterValue ?? DBNull.Value;
            command.Parameters.Add(filterParameter);
        }

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (var index = 0; index < reader.FieldCount; index++)
                row.Add(reader.GetName(index), await reader.IsDBNullAsync(index) ? null : reader.GetValue(index));

            RedactPiiColumns(row, piiColumns);
            rows.Add(row);
        }

        return JsonSerializer.Serialize(rows);
    }

    internal static void RedactPiiColumns(Dictionary<string, object?> row, IEnumerable<string> piiColumns)
    {
        foreach (var column in piiColumns) row[column] = RedactedMarker;
    }

    private static string QuoteIdentifier(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    internal static bool IsAllowedColumn(IModel model, string schema, string table, string columnName) =>
        GetColumnExposure(model, schema, table, columnName) is McpFieldExposure.Safe or McpFieldExposure.Redact;

    internal static bool IsPiiColumn(IModel model, string schema, string table, string columnName) =>
        GetColumnExposure(model, schema, table, columnName) is McpFieldExposure.Redact;

    private static McpFieldExposure GetColumnExposure(IModel model, string schema, string table, string columnName)
    {
        var entityType = model.GetEntityTypes().FirstOrDefault(entity =>
            entity.GetSchema()?.Equals(schema, StringComparison.OrdinalIgnoreCase) == true &&
            entity.GetTableName()?.Equals(table, StringComparison.OrdinalIgnoreCase) == true);
        if (entityType is null) return McpFieldExposure.Exclude;

        var storeObject = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        var property = entityType.GetProperties().FirstOrDefault(candidate =>
            candidate.GetColumnName(storeObject)?.Equals(columnName, StringComparison.OrdinalIgnoreCase) == true);
        if (property is null) return McpFieldExposure.Exclude;

        var value = property.FindAnnotation(McpEntityExposure.AnnotationName)?.Value?.ToString();
        // Fail closed: a property with no exposure annotation (or an unparsable one) is excluded,
        // not treated as safe, so newly scaffolded columns are hidden until deliberately classified.
        return Enum.TryParse<McpFieldExposure>(value, out var exposure)
            ? exposure
            : McpFieldExposure.Exclude;
    }
}