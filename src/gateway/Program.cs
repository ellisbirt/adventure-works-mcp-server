// src/gateway/Program.cs
using System.Text.Json;
using EnterpriseAiGateway.Core.DTOs;
using EnterpriseAiGateway.Data.Models;
using EnterpriseAiGateway.Data.Repositories;
using EnterpriseAiGateway.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.ApplicationInsights.Extensibility;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationInsightsTelemetry();
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    }
}));

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
    options.UseSqlServer(builder.Configuration.GetConnectionString("AdventureWorksConnection")));

builder.Services.AddScoped<ISecureCustomerRepository, SecureCustomerRepository>();

var app = builder.Build();
app.UseCors();

// Endpoints mapping the Model Context Protocol Schema Specifications
// Endpoint A: Exposes the schema of supported tools to Claude
app.MapGet("/mcp/tools", (ILogger<Program> logger) =>
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
});

// Endpoint B: Handles execution calls routed from the LLM client engine
app.MapPost("/mcp/tools/call", async (
    McpCallToolRequest request,
    ISecureCustomerRepository repo,
    ILogger<Program> logger) =>
{
    if (request.Name != "get_customer_history")
    {
        GatewayLogMessages.UnknownTool(logger, request.Name);

        return Results.BadRequest(new McpCallToolResponse(
            new List<McpContentText> { new("text", "Error: The requested tool is not mapped to this gateway server profile.") },
            IsError: true
        ));
    }

    // Safely extract parameter elements from the request package arguments dictionary
    if (!request.Arguments.TryGetValue("customerId", out var rawId) || 
        !int.TryParse(rawId.ToString(), out int customerId))
    {
        GatewayLogMessages.InvalidCustomerId(logger);

        return Results.BadRequest(new McpCallToolResponse(
            new List<McpContentText> { new("text", "Error: Missing or malformed required argument: customerId.") },
            IsError: true
        ));
    }

    // Governance Flag: Force strict PII masking boundary for standard execution loops
    bool enforcePiiMasking = true; 
    GatewayLogMessages.CustomerHistoryRequested(logger, customerId);
    
    var dataContext = await repo.GetCustomerContextAsync(customerId, enforcePiiMasking);

    return Results.Ok(new McpCallToolResponse(
        new List<McpContentText> { new("text", dataContext) }
    ));
});

app.Run();
