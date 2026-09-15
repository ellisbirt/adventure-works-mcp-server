using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using EnterpriseAiGateway.Data.Scaffolded;

namespace EnterpriseAiGateway.Data.Repositories;

public record SafeTableDefinition(string Schema, string Name, IReadOnlyList<string> Columns);

public interface ISecureTableCatalogRepository
{
    Task<IReadOnlyList<SafeTableDefinition>> GetTablesAsync();
    Task<string> GetTableRowsAsync(string schema, string table, int limit);
}

public sealed class SecureTableCatalogRepository : ISecureTableCatalogRepository
{
    private static readonly HashSet<string> SensitiveColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "AddressLine1", "AddressLine2", "City", "CountryRegion", "EmailAddress", "FirstName",
        "LastName", "MiddleName", "PasswordHash", "PasswordSalt", "Phone", "PostalCode",
        "StateProvince", "Suffix", "Title"
    };

    private readonly AdventureWorksDbContext _context;

    public SecureTableCatalogRepository(AdventureWorksDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<SafeTableDefinition>> GetTablesAsync()
    {
        await using var connection = _context.Database.GetDbConnection();
        await connection.OpenAsync();
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
            var key = (reader.GetString(0), reader.GetString(1));
            if (!tables.TryGetValue(key, out var columns))
            {
                columns = [];
                tables.Add(key, columns);
            }

            var column = reader.GetString(2);
            if (!IsSensitiveColumn(column)) columns.Add(column);
        }

        return tables
            .Where(table => table.Value.Count > 0)
            .Select(table => new SafeTableDefinition(table.Key.Schema, table.Key.Name, table.Value))
            .ToList();
    }

    public async Task<string> GetTableRowsAsync(string schema, string table, int limit)
    {
        var tableDefinition = (await GetTablesAsync()).FirstOrDefault(candidate =>
            candidate.Schema.Equals(schema, StringComparison.OrdinalIgnoreCase) &&
            candidate.Name.Equals(table, StringComparison.OrdinalIgnoreCase));

        if (tableDefinition is null)
            return "Error: The requested table is unavailable or contains no non-sensitive columns.";

        await using var connection = _context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT TOP (@limit) {string.Join(", ", tableDefinition.Columns.Select(QuoteIdentifier))} FROM {QuoteIdentifier(tableDefinition.Schema)}.{QuoteIdentifier(tableDefinition.Name)};";
        var limitParameter = command.CreateParameter();
        limitParameter.ParameterName = "@limit";
        limitParameter.DbType = DbType.Int32;
        limitParameter.Value = limit;
        command.Parameters.Add(limitParameter);

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (var index = 0; index < reader.FieldCount; index++)
                row.Add(reader.GetName(index), await reader.IsDBNullAsync(index) ? null : reader.GetValue(index));
            rows.Add(row);
        }

        return JsonSerializer.Serialize(rows);
    }

    private static string QuoteIdentifier(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    public static bool IsSensitiveColumn(string columnName)
    {
        if (SensitiveColumns.Contains(columnName)) return true;

        var normalizedName = columnName.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalizedName.Contains("address", StringComparison.Ordinal) ||
               normalizedName.Contains("birth", StringComparison.Ordinal) ||
               normalizedName.Contains("card", StringComparison.Ordinal) ||
               normalizedName.Contains("email", StringComparison.Ordinal) ||
               normalizedName.Contains("password", StringComparison.Ordinal) ||
               normalizedName.Contains("phone", StringComparison.Ordinal) ||
               normalizedName.Contains("postal", StringComparison.Ordinal) ||
               normalizedName.Contains("secret", StringComparison.Ordinal) ||
               normalizedName.Contains("socialsecurity", StringComparison.Ordinal) ||
               normalizedName.Contains("ssn", StringComparison.Ordinal) ||
               normalizedName.Contains("token", StringComparison.Ordinal) ||
               normalizedName.Contains("username", StringComparison.Ordinal) ||
               normalizedName.Contains("ipaddress", StringComparison.Ordinal) ||
               normalizedName.Contains("bankaccount", StringComparison.Ordinal);
    }
}