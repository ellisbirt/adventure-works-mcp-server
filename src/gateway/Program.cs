// src/gateway/Program.cs
using System.Text.Json;
using EnterpriseAiGateway.Core.DTOs;
using EnterpriseAiGateway.Data.Models;
using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Data.Interceptors;
using EnterpriseAiGateway.Infrastructure;
using EnterpriseAiGateway.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

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
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

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
builder.Services.AddDbContext<AdventureWorksContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AdventureWorksConnection"))
        .AddInterceptors(new CorrelationCommandInterceptor()));

builder.Services.AddScoped<ISecureCustomerRepository, SecureCustomerRepository>();

var app = builder.Build();
app.UseMiddleware<CorrelationMiddleware>();
app.UseCors();
app.UseRateLimiter();

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
            )
        };

        return Results.Ok(new McpListToolsResponse(toolsList));
    }).RequireRateLimiting("mcp");

    routes.MapPost("/mcp/tools/call", async (
        McpCallToolRequest request,
        ISecureCustomerRepository repo,
        ILogger<Program> logger) =>
    {
        if (request.Arguments is null || request.Name != "get_customer_history")
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

// Versioned API is canonical; legacy paths remain as compatibility aliases.
MapMcpEndpoints(app.MapGroup("/api/v1"));
MapMcpEndpoints(app);

app.Run();
