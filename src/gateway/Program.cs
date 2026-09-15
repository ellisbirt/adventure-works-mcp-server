// src/gateway/Program.cs
using System.Text.Json;
using EnterpriseAiGateway.Core.DTOs;
using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Data.Scaffolded;
using EnterpriseAiGateway.Data.Interceptors;
using EnterpriseAiGateway.Infrastructure;
using EnterpriseAiGateway.Integration.Anthropic;
using EnterpriseAiGateway.Integration.Chat;
using EnterpriseAiGateway.Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var authenticationAuthority = builder.Configuration["Authentication:Authority"];
var authenticationAudience = builder.Configuration["Authentication:Audience"];
var authenticationScope = builder.Configuration["Authentication:RequiredScope"] ?? "access_as_user";
var authenticationRequired = builder.Configuration.GetValue<bool>("Authentication:Enabled");
var authenticationEnabled = !string.IsNullOrWhiteSpace(authenticationAuthority) && !string.IsNullOrWhiteSpace(authenticationAudience);
if (builder.Environment.IsProduction() && !authenticationRequired)
    throw new InvalidOperationException("Authentication:Enabled must be true in Production.");
if (authenticationRequired && !authenticationEnabled)
    throw new InvalidOperationException("Authentication:Authority and Authentication:Audience are required when authentication is enabled.");

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32 * 1024);

builder.Services.AddApplicationInsightsTelemetry();
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    }
}));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("mcp", context => RateLimitPartition.GetFixedWindowLimiter(
        GetRateLimitPartitionKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("chat", context => RateLimitPartition.GetFixedWindowLimiter(
        GetRateLimitPartitionKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

if (authenticationRequired)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authenticationAuthority;
            options.Audience = authenticationAudience;
            options.MapInboundClaims = false;
        });
    builder.Services.AddAuthorization(options => options.AddPolicy("gateway-api", policy =>
        policy.RequireAuthenticatedUser().RequireAssertion(context =>
            context.User.Claims.Any(claim => claim.Type == "scp" &&
                claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(authenticationScope, StringComparer.Ordinal)))));
}

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.ApplicationInsights(
            services.GetRequiredService<TelemetryConfiguration>(),
            TelemetryConverter.Traces);
});

// Connect your database connection strings directly to your live Azure SQL or local fallback container
builder.Services.AddDbContext<AdventureWorksDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AdventureWorksConnection"))
        .AddInterceptors(new CorrelationCommandInterceptor()));

builder.Services.AddScoped<ISecureCustomerRepository, SecureCustomerRepository>();
builder.Services.AddScoped<ISecureTableCatalogRepository, SecureTableCatalogRepository>();
if (!string.IsNullOrWhiteSpace(builder.Configuration["Anthropic:ApiKey"]))
{
    builder.Services.AddAnthropicClient(builder.Configuration);
    builder.Services.AddScoped<IMcpChatService, McpChatService>();
}

var app = builder.Build();
app.UseMiddleware<CorrelationMiddleware>();
app.UseCors();
if (authenticationRequired)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new HealthResponse(
    Status: "alive",
    Liveness: true,
    Readiness: null,
    Database: null,
    Anthropic: null,
    Details: Array.Empty<string>())))
    .ExcludeFromDescription();

app.MapGet("/health/ready", async (
    AdventureWorksDbContext db,
    IConfiguration configuration,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var databaseReady = false;
    var databaseFailureReason = "database_unreachable";
    var databaseTimeoutSeconds = Math.Clamp(
        configuration.GetValue<int?>("Health:DatabaseTimeoutSeconds") ?? 5,
        1,
        30);
    try
    {
        using var databaseCheckCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        databaseCheckCancellation.CancelAfter(TimeSpan.FromSeconds(databaseTimeoutSeconds));
        databaseReady = await db.Database.CanConnectAsync(databaseCheckCancellation.Token);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        databaseFailureReason = "database_timeout";
        logger.LogWarning("Gateway readiness database check timed out after {TimeoutSeconds}s.", databaseTimeoutSeconds);
    }
    catch (Exception exception)
    {
        logger.LogWarning(exception, "Gateway readiness database check failed.");
    }

    var anthropicReady = !string.IsNullOrWhiteSpace(configuration["Anthropic:ApiKey"]) &&
                         !string.IsNullOrWhiteSpace(configuration["Anthropic:Model"]);
    var reasons = new List<string>();
    if (!databaseReady) reasons.Add(databaseFailureReason);
    if (!anthropicReady) reasons.Add("anthropic_configuration_missing");

    if (reasons.Count > 0)
    {
        return Results.Json(new HealthResponse(
            Status: "not_ready",
            Liveness: true,
            Readiness: false,
            Database: databaseReady,
            Anthropic: anthropicReady,
            Details: reasons),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new HealthResponse(
        Status: "ready",
        Liveness: true,
        Readiness: true,
        Database: true,
        Anthropic: true,
        Details: Array.Empty<string>()));
}).ExcludeFromDescription();

string GetRateLimitPartitionKey(HttpContext context) =>
    context.User.FindFirst("sub")?.Value ??
    context.User.FindFirst("oid")?.Value ??
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

void MapMcpEndpoints(IEndpointRouteBuilder routes)
{
    routes.MapPost("/mcp", async (
        HttpRequest httpRequest,
        ISecureCustomerRepository repo,
        ISecureTableCatalogRepository tableCatalog,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return Results.Json(new McpJsonRpcResponse("2.0", JsonNullId(), Error: new McpJsonRpcError(-32700, "Parse error.")), statusCode: StatusCodes.Status400BadRequest);
        }

        using (document)
        {
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                if (document.RootElement.GetArrayLength() == 0)
                    return Results.Json(new McpJsonRpcResponse("2.0", JsonNullId(), Error: new McpJsonRpcError(-32600, "Invalid Request.")), statusCode: StatusCodes.Status400BadRequest);

                var responses = new List<McpJsonRpcResponse>();
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    var response = await ProcessMcpRequestAsync(item, repo, tableCatalog, logger, cancellationToken);
                    if (response is not null) responses.Add(response);
                }

                return responses.Count == 0 ? Results.NoContent() : Results.Json(responses);
            }

            var singleResponse = await ProcessMcpRequestAsync(document.RootElement, repo, tableCatalog, logger, cancellationToken);
            return singleResponse is null ? Results.NoContent() : Results.Json(singleResponse);
        }
    }).RequireRateLimiting("mcp");
}

static JsonElement JsonNullId() => JsonDocument.Parse("null").RootElement.Clone();

async Task<McpJsonRpcResponse?> ProcessMcpRequestAsync(
    JsonElement element,
    ISecureCustomerRepository repo,
    ISecureTableCatalogRepository tableCatalog,
    ILogger<Program> logger,
    CancellationToken cancellationToken)
{
    McpJsonRpcRequest? request;
    try
    {
        request = element.Deserialize<McpJsonRpcRequest>();
    }
    catch (JsonException)
    {
        return new McpJsonRpcResponse("2.0", JsonNullId(), Error: new McpJsonRpcError(-32600, "Invalid Request."));
    }

    if (request is null)
        return new McpJsonRpcResponse("2.0", JsonNullId(), Error: new McpJsonRpcError(-32600, "Invalid Request."));

    if (request.JsonRpc != "2.0" || string.IsNullOrWhiteSpace(request.Method) ||
        (request.Id.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) &&
         request.Id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)))
        return new McpJsonRpcResponse("2.0", request.Id.ValueKind == JsonValueKind.Undefined ? JsonNullId() : request.Id, Error: new McpJsonRpcError(-32600, "Invalid Request."));

    var id = request.Id.ValueKind == JsonValueKind.Undefined ? JsonNullId() : request.Id.Clone();
    McpJsonRpcResponse? Response(object result) => request.IsNotification ? null : new McpJsonRpcResponse("2.0", id, Result: result);
    McpJsonRpcResponse? Error(int code, string message) => request.IsNotification ? null : new McpJsonRpcResponse("2.0", id, Error: new McpJsonRpcError(code, message));

    if (request.Method == "notifications/initialized") return null;

    if (request.Method == "initialize")
    {
        var protocolVersion = "2025-06-18";
        if (request.Params.ValueKind == JsonValueKind.Object && request.Params.TryGetProperty("protocolVersion", out var requestedVersion) &&
            requestedVersion.ValueKind == JsonValueKind.String && requestedVersion.GetString() is "2024-11-05" or "2025-06-18")
            protocolVersion = requestedVersion.GetString()!;

        return Response(new
        {
            protocolVersion,
            capabilities = new { tools = new { } },
            serverInfo = new { name = "enterprise-ai-gateway", version = "1.0.0" }
        });
    }

    if (request.Method == "tools/list")
    {
        GatewayLogMessages.ToolsRequested(logger);
        return Response(new { tools = BuildMcpTools() });
    }

    if (request.Method != "tools/call") return Error(-32601, "Method not found.");
    if (request.Params.ValueKind != JsonValueKind.Object || !request.Params.TryGetProperty("name", out var nameElement) ||
        nameElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(nameElement.GetString()))
        return Error(-32602, "Invalid params.");

    var arguments = new Dictionary<string, object>();
    if (request.Params.TryGetProperty("arguments", out var argumentsElement))
    {
        if (argumentsElement.ValueKind != JsonValueKind.Object)
            return Error(-32602, "Invalid params.");
        foreach (var property in argumentsElement.EnumerateObject()) arguments[property.Name] = property.Value.Clone();
    }

    var toolResponse = await ExecuteMcpToolAsync(nameElement.GetString()!, arguments, repo, tableCatalog, logger);
    return Response(toolResponse);
}

List<McpToolDefinition> BuildMcpTools() =>
[
    new("get_customer_history", "Safely reads strongly-typed historical relational summaries for a specific customer from the database schemas.", new McpInputSchema(Properties: new Dictionary<string, McpPropertyDefinition> { ["customerId"] = new("integer", "The unique identity key integer of the customer entity.") }, Required: ["customerId"])),
    new("list_database_tables", "Lists every database table and its useful columns available to the model; personal fields are redacted.", new McpInputSchema()),
    new("read_database_table", "Reads up to 100 rows from an available database table; personal fields are redacted and marked [REDACTED].", new McpInputSchema(Properties: new Dictionary<string, McpPropertyDefinition> { ["schema"] = new("string", "Schema returned by list_database_tables."), ["table"] = new("string", "Table name returned by list_database_tables."), ["limit"] = new("integer", "Optional number of rows to return, from 1 through 100.") }, Required: ["schema", "table"]))
];

async Task<McpCallToolResponse> ExecuteMcpToolAsync(
    string name,
    Dictionary<string, object> arguments,
    ISecureCustomerRepository repo,
    ISecureTableCatalogRepository tableCatalog,
    ILogger<Program> logger)
{
    if (name == "list_database_tables")
        return new([new("text", JsonSerializer.Serialize(await tableCatalog.GetTablesAsync()))]);

    if (name == "read_database_table")
    {
        if (!arguments.TryGetValue("schema", out var rawSchema) || !arguments.TryGetValue("table", out var rawTable) ||
            string.IsNullOrWhiteSpace(rawSchema.ToString()) || string.IsNullOrWhiteSpace(rawTable.ToString()))
            return new([new("text", "Error: Missing required arguments: schema and table.")], true);

        var limit = 20;
        if (arguments.TryGetValue("limit", out var rawLimit) && (!int.TryParse(rawLimit.ToString(), out limit) || limit is < 1 or > 100))
            return new([new("text", "Error: limit must be an integer from 1 through 100.")], true);

        var rows = await tableCatalog.GetTableRowsAsync(rawSchema.ToString()!, rawTable.ToString()!, limit);
        return new([new("text", rows)]);
    }

    if (name != "get_customer_history")
    {
        GatewayLogMessages.UnknownTool(logger, name);
        return new([new("text", "Error: The requested tool is not mapped to this gateway server profile.")], true);
    }

    if (!arguments.TryGetValue("customerId", out var rawId) || !int.TryParse(rawId.ToString(), out var customerId) || customerId <= 0)
    {
        GatewayLogMessages.InvalidCustomerId(logger);
        return new([new("text", "Error: Missing or malformed required argument: customerId.")], true);
    }

    GatewayLogMessages.CustomerHistoryRequested(logger, customerId);
    return new([new("text", await repo.GetCustomerContextAsync(customerId))]);
}

void MapChatEndpoints(IEndpointRouteBuilder routes)
{
    routes.MapPost("/chat", async Task<IResult> (ChatRequest request, IServiceProvider services, CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.Message)) return Results.BadRequest(new { error = "A chat message is required." });

        var chatService = services.GetService<IMcpChatService>();
        if (chatService is null) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        try
        {
            var response = await chatService.AskAsync(request.Message, cancellationToken);
            return Results.Ok(new ChatResponse(response.Message, response.Tool));
        }
        catch (JsonException)
        {
            return Results.Json(new { error = "The AI returned an invalid MCP tool selection." }, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (InvalidOperationException)
        {
            return Results.Json(new { error = "The database assistant could not complete the request." }, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (AnthropicProviderUnavailableException)
        {
            return Results.Json(new { error = "The AI provider is temporarily unavailable. Please try again shortly." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (AnthropicProviderResponseException)
        {
            return Results.Json(new { error = "The AI provider returned an invalid response." }, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }).RequireRateLimiting("chat");
}

var api = app.MapGroup("/api/v1");
if (authenticationRequired) api.RequireAuthorization("gateway-api");
MapMcpEndpoints(api);
MapChatEndpoints(api);

app.Run();

internal sealed record HealthResponse(
    string Status,
    bool Liveness,
    bool? Readiness,
    bool? Database,
    bool? Anthropic,
    IReadOnlyList<string> Details);
