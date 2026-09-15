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

    // Per-table policy of which columns are useful enough to return, and which of those
    // are personal/sensitive and must be redacted rather than omitted. Credential material
    // (password hash/salt) and internal surrogate keys (rowguid) are left out of both lists
    // entirely: they are never useful to a caller, so they are never returned at all.
    // Tables absent from this map expose no columns and must be added here deliberately.
    private static readonly Dictionary<string, Dictionary<string, ColumnPolicy>> ColumnPolicyByTable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SalesLT"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Address"] = Policy(
                    safeColumns: ["AddressID", "City", "StateProvince", "CountryRegion", "ModifiedDate"],
                    piiColumns: ["AddressLine1", "AddressLine2", "PostalCode"]),
                ["Customer"] = Policy(
                    safeColumns: ["CustomerID", "NameStyle", "SalesPerson", "ModifiedDate"],
                    piiColumns: ["Title", "FirstName", "MiddleName", "LastName", "Suffix", "CompanyName", "EmailAddress", "Phone"]),
                ["CustomerAddress"] = Policy(
                    safeColumns: ["CustomerID", "AddressID", "AddressType", "ModifiedDate"]),
                ["Product"] = Policy(
                    safeColumns: [
                        "ProductID", "Name", "ProductNumber", "Color", "StandardCost", "ListPrice",
                        "Size", "Weight", "ProductCategoryID", "ProductModelID", "SellStartDate",
                        "SellEndDate", "DiscontinuedDate", "ModifiedDate"]),
                ["ProductCategory"] = Policy(
                    safeColumns: ["ProductCategoryID", "ParentProductCategoryID", "Name", "ModifiedDate"]),
                ["ProductDescription"] = Policy(
                    safeColumns: ["ProductDescriptionID", "Description", "ModifiedDate"]),
                ["ProductModel"] = Policy(
                    safeColumns: ["ProductModelID", "Name", "ModifiedDate"]),
                ["ProductModelProductDescription"] = Policy(
                    safeColumns: ["ProductModelID", "ProductDescriptionID", "Culture", "ModifiedDate"]),
                ["SalesOrderDetail"] = Policy(
                    safeColumns: [
                        "SalesOrderID", "SalesOrderDetailID", "OrderQty", "ProductID", "UnitPrice",
                        "UnitPriceDiscount", "LineTotal", "ModifiedDate"]),
                ["SalesOrderHeader"] = Policy(
                    safeColumns: [
                        "SalesOrderID", "RevisionNumber", "OrderDate", "DueDate", "ShipDate", "Status",
                        "OnlineOrderFlag", "SalesOrderNumber", "PurchaseOrderNumber", "AccountNumber",
                        "CustomerID", "ShipToAddressID", "BillToAddressID", "ShipMethod", "SubTotal",
                        "TaxAmt", "Freight", "TotalDue", "Comment", "ModifiedDate"],
                    piiColumns: ["CreditCardApprovalCode"]),
            },
        };

    private sealed record ColumnPolicy(IReadOnlyList<string> Columns, IReadOnlySet<string> PiiColumns);

    private static ColumnPolicy Policy(string[] safeColumns, string[]? piiColumns = null)
    {
        var pii = piiColumns ?? [];
        return new ColumnPolicy([.. safeColumns, .. pii], new HashSet<string>(pii, StringComparer.OrdinalIgnoreCase));
    }

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
            (string Schema, string Name) key = (reader.GetString(0), reader.GetString(1));
            if (!tables.TryGetValue(key, out var columns))
            {
                columns = [];
                tables.Add(key, columns);
            }

            var column = reader.GetString(2);
            if (IsAllowedColumn(key.Schema, key.Name, column)) columns.Add(column);
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
        var queryColumns = tableDefinition.Columns.Where(column => !IsPiiColumn(schema, table, column)).ToList();
        var piiColumns = tableDefinition.Columns.Where(column => IsPiiColumn(schema, table, column)).ToList();

        await using var connection = _context.Database.GetDbConnection();
        await connection.OpenAsync();
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

    public static bool IsAllowedColumn(string schema, string table, string columnName) =>
        ColumnPolicyByTable.TryGetValue(schema, out var tables) &&
        tables.TryGetValue(table, out var policy) &&
        policy.Columns.Contains(columnName, StringComparer.OrdinalIgnoreCase);

    public static bool IsPiiColumn(string schema, string table, string columnName) =>
        ColumnPolicyByTable.TryGetValue(schema, out var tables) &&
        tables.TryGetValue(table, out var policy) &&
        policy.PiiColumns.Contains(columnName);
}