// src/gateway/Data/Repositories/SecureCustomerRepository.cs
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using EnterpriseAiGateway.Data.Models;

namespace EnterpriseAiGateway.Data.Repositories;

/// <summary>
/// Defines the contract for secure customer data retrieval operations.
/// Acts as the data-grounding boundary between the LLM infrastructure and relational storage,
/// enforcing encryption and masking policies for AI model consumption.
/// </summary>
public interface ISecureCustomerRepository
{
    /// <summary>
    /// Retrieves customer context asynchronously with optional PII masking for LLM prompts.
    /// </summary>
    /// <param name="customerId">The unique customer identifier from AdventureWorks dataset</param>
    /// <param name="maskSensitiveData">
    /// When true, applies regex-based redaction to EmailAddress and Phone properties
    /// to prevent sensitive data leakage into AI model context windows
    /// </param>
    /// <returns>
    /// A formatted string summary suitable for LLM ingestion, containing customer metadata
    /// with optional PII masking applied
    /// </returns>
    Task<string> GetCustomerContextAsync(int customerId, bool maskSensitiveData);
}

/// <summary>
/// Repository implementation enforcing secure data access patterns for customer entities.
/// Utilizes Entity Framework Core with AsNoTracking for gateway-optimized query performance.
/// 
/// Thread Safety:
/// - All Regex patterns are pre-compiled and static, ensuring thread-safe concurrent execution
/// - DbContext is scoped per request, preventing multi-threaded state corruption
/// 
/// Performance Optimizations:
/// - AsNoTracking() eliminates change tracking overhead for read-only gateway operations
/// - Static Regex compilation with RegexOptions.Compiled caches NFA automata in memory
/// - FirstOrDefaultAsync() returns fast with index seeks on CustomerID primary key
/// </summary>
public class SecureCustomerRepository : ISecureCustomerRepository
{
    private readonly AdventureWorksContext _context;
    
    /// <summary>
    /// Pre-compiled, thread-safe regex engine for email address pattern matching.
    /// Pattern: [\w\.-]+@[\w\.-]+\.\w+ matches standard RFC 5322 email format.
    /// RegexOptions.Compiled caches the NFA state machine in memory for repeated calls.
    /// RegexOptions.IgnoreCase ensures case-insensitive matching for email domains.
    /// </summary>
    private static readonly Regex EmailRegex = new(
        @"[\w\.-]+@[\w\.-]+\.\w+", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    
    /// <summary>
    /// Pre-compiled, thread-safe regex engine for North American phone number format.
    /// Pattern: \d{3}-\d{3}-\d{4} matches XXX-XXX-XXXX format common in AdventureWorks.
    /// RegexOptions.Compiled ensures high-throughput masking performance in gateway.
    /// </summary>
    private static readonly Regex PhoneRegex = new(
        @"\d{3}-\d{3}-\d{4}", 
        RegexOptions.Compiled
    );

    /// <summary>
    /// Initializes a new instance of the SecureCustomerRepository with dependency-injected DbContext.
    /// Dependency injection enables testability and decouples repository from DbContext instantiation.
    /// </summary>
    /// <param name="context">The AdventureWorksContext providing access to relational storage</param>
    public SecureCustomerRepository(AdventureWorksContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Retrieves customer context with asynchronous, non-tracking query execution.
    /// 
    /// Data Protection:
    /// - AsNoTracking() prevents EF Core from retaining entities in memory between requests
    /// - This boundary ensures the gateway never exposes raw database objects to the AI model
    /// - Masking applies before string serialization to prevent information leakage
    /// 
    /// Corporate Governance:
    /// - When maskSensitiveData=true, applies PII redaction per enterprise data loss prevention (DLP) policies
    /// - Redacted values prevent sensitive customer data from training or influencing LLM outputs
    /// </summary>
    public async Task<string> GetCustomerContextAsync(int customerId, bool maskSensitiveData)
    {
        // AsNoTracking optimization avoids runtime memory footprint overhead for gateway proxying
        // Scans CustomerID primary key index with single row seek + materialize
        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerID == customerId);

        // Early return for missing customers prevents null reference errors downstream
        if (customer == null)
        {
            return $"System Alert: Customer ID {customerId} not found in relational storage schemas.";
        }

        // Pre-allocate mutable copies to avoid modifying source customer properties
        var email = customer.EmailAddress;
        var phone = customer.Phone;

        // If corporate governance claims require masking, apply thread-safe regex redaction
        // String.IsNullOrEmpty guards prevent regex allocation for null/empty properties
        if (maskSensitiveData)
        {
            email = !string.IsNullOrEmpty(email) 
                ? EmailRegex.Replace(email, "[REDACTED_EMAIL]") 
                : email;
                
            phone = !string.IsNullOrEmpty(phone) 
                ? PhoneRegex.Replace(phone, "[REDACTED_PHONE]") 
                : phone;
        }

        // Return formatted text summary suitable for LLM model context window consumption
        // Pipe-delimited format ensures easy parsing by downstream AI prompt injectors
        return $"Customer Entity Record Detected -> ID: {customer.CustomerID} | Name: {customer.FirstName} {customer.LastName} | Company: {customer.CompanyName} | Contact: {phone} / {email}";
    }
}