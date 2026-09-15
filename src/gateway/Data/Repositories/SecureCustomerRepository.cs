using System.Text.Json;

namespace EnterpriseAiGateway.Data.Repositories;

/// <summary>
/// Data-grounding boundary for single-customer lookups used by the get_customer_history tool.
/// </summary>
public interface ISecureCustomerRepository
{
    /// <summary>
    /// Returns a formatted summary of the requested customer using only the columns the
    /// safe MCP catalog allow-lists for the Customer table.
    /// </summary>
    Task<string> GetCustomerContextAsync(int customerId);
}

/// <summary>
/// Routes customer lookups through the same schema-derived, per-table column allow-list as
/// generic table reads, so PII is never selected in the first place rather than redacted after the fact.
/// </summary>
public sealed class SecureCustomerRepository : ISecureCustomerRepository
{
    private const string Schema = "SalesLT";
    private const string Table = "Customer";

    private readonly ISecureTableCatalogRepository _tableCatalog;

    public SecureCustomerRepository(ISecureTableCatalogRepository tableCatalog)
    {
        _tableCatalog = tableCatalog ?? throw new ArgumentNullException(nameof(tableCatalog));
    }

    public async Task<string> GetCustomerContextAsync(int customerId)
    {
        var rowsJson = await _tableCatalog.GetTableRowsAsync(Schema, Table, limit: 1, filterColumn: "CustomerID", filterValue: customerId);
        using var document = JsonDocument.Parse(rowsJson);
        var row = document.RootElement.EnumerateArray().FirstOrDefault();

        if (row.ValueKind != JsonValueKind.Object)
            return $"System Alert: Customer ID {customerId} not found in relational storage schemas.";

        var fields = row.EnumerateObject().Select(property => $"{property.Name}: {property.Value}");
        return $"Customer Entity Record Detected -> {string.Join(" | ", fields)}";
    }
}