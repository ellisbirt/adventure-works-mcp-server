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

string GetRateLimitPartitionKey(HttpContext context) =>
    context.User.FindFirst("sub")?.Value ??
    context.User.FindFirst("oid")?.Value ??
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

void MapMcpEndpoints(IEndpointRouteBuilder routes)
{
    routes.MapGet("/mcp/tools", (ILogger<Program> logger) =>
    {
        GatewayLogMessages.ToolsRequested(logger);

        var toolsList = new List<McpToolDefinition>
        {
            new(
                Name: "get_customer_history",
                Description: "Safely reads strongly-typed historical relational summaries for a specific customer from the database schemas.",
                InputSchema: new McpInputSchema(
                    Properties: new Dictionary<string, McpPropertyDefinition>
                    {
                        { "customerId", new McpPropertyDefinition("integer", "The unique identity key integer of the customer entity.") }
                    },
                    Required: new List<string> { "customerId" }
                )
            ),
            new(
                Name: "list_database_tables",
                Description: "Lists every database table and the non-sensitive columns available to the model.",
                InputSchema: new McpInputSchema()
            ),
            new(
                Name: "read_database_table",
                Description: "Reads up to 100 rows from an available database table, excluding personal information.",
                InputSchema: new McpInputSchema(
                    Properties: new Dictionary<string, McpPropertyDefinition>
                    {
                        { "schema", new McpPropertyDefinition("string", "Schema returned by list_database_tables.") },
                        { "table", new McpPropertyDefinition("string", "Table name returned by list_database_tables.") },
                        { "limit", new McpPropertyDefinition("integer", "Optional number of rows to return, from 1 through 100.") }
                    },
                    Required: new List<string> { "schema", "table" }
                )
            )
        };

        return Results.Ok(new McpListToolsResponse(toolsList));
    }).RequireRateLimiting("mcp");

    routes.MapPost("/mcp/tools/call", async (
        McpCallToolRequest request,
        ISecureCustomerRepository repo,
        ISecureTableCatalogRepository tableCatalog,
        ILogger<Program> logger) =>
    {
        if (request.Arguments is null)
        {
            GatewayLogMessages.UnknownTool(logger, request.Name);

            return Results.BadRequest(new McpCallToolResponse(
                new List<McpContentText> { new("text", "Error: The requested tool is not mapped to this gateway server profile.") },
                IsError: true
            ));
        }

        if (request.Name == "list_database_tables")
        {
            var tables = await tableCatalog.GetTablesAsync();
            return Results.Ok(new McpCallToolResponse(
                new List<McpContentText> { new("text", System.Text.Json.JsonSerializer.Serialize(tables)) }
            ));
        }

        if (request.Name == "read_database_table")
        {
            if (!request.Arguments.TryGetValue("schema", out var rawSchema) ||
                !request.Arguments.TryGetValue("table", out var rawTable) ||
                string.IsNullOrWhiteSpace(rawSchema?.ToString()) || string.IsNullOrWhiteSpace(rawTable?.ToString()))
            {
                return Results.BadRequest(new McpCallToolResponse(
                    new List<McpContentText> { new("text", "Error: Missing required arguments: schema and table.") },
                    IsError: true
                ));
            }

            var limit = 20;
            if (request.Arguments.TryGetValue("limit", out var rawLimit) &&
                (!int.TryParse(rawLimit?.ToString(), out limit) || limit is < 1 or > 100))
            {
                return Results.BadRequest(new McpCallToolResponse(
                    new List<McpContentText> { new("text", "Error: limit must be an integer from 1 through 100.") },
                    IsError: true
                ));
            }

            var rows = await tableCatalog.GetTableRowsAsync(rawSchema!.ToString()!, rawTable!.ToString()!, limit);
            return Results.Ok(new McpCallToolResponse(new List<McpContentText> { new("text", rows) }));
        }

        if (request.Name != "get_customer_history")
        {
            GatewayLogMessages.UnknownTool(logger, request.Name);
            return Results.BadRequest(new McpCallToolResponse(
                new List<McpContentText> { new("text", "Error: The requested tool is not mapped to this gateway server profile.") },
                IsError: true
            ));
        }

        if (!request.Arguments.TryGetValue("customerId", out var rawId) ||
            !int.TryParse(rawId?.ToString(), out int customerId) || customerId <= 0)
        {
            GatewayLogMessages.InvalidCustomerId(logger);

            return Results.BadRequest(new McpCallToolResponse(
                new List<McpContentText> { new("text", "Error: Missing or malformed required argument: customerId.") },
                IsError: true
            ));
        }

        GatewayLogMessages.CustomerHistoryRequested(logger, customerId);
        var dataContext = await repo.GetCustomerContextAsync(customerId, maskSensitiveData: true);

        return Results.Ok(new McpCallToolResponse(
            new List<McpContentText> { new("text", dataContext) }
        ));
    }).RequireRateLimiting("mcp");
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
        catch (InvalidOperationException exception)
        {
            return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }).RequireRateLimiting("chat");
}

// Versioned API is canonical; legacy paths remain as compatibility aliases.
var api = app.MapGroup("/api/v1");
if (authenticationRequired) api.RequireAuthorization("gateway-api");
MapMcpEndpoints(api);
MapChatEndpoints(api);
var legacyApi = app.MapGroup("");
if (authenticationRequired) legacyApi.RequireAuthorization("gateway-api");
MapMcpEndpoints(legacyApi);
MapChatEndpoints(legacyApi);

app.Run();
