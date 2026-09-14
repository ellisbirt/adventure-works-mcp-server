using System.Diagnostics;
using Microsoft.Extensions.Primitives;

namespace EnterpriseAiGateway.Infrastructure;

public sealed class CorrelationMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationMiddleware> _logger;

    public CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetCorrelationId(context.Request.Headers[HeaderName]);
        CorrelationContext.Id = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }

        CorrelationContext.Id = null;
    }

    private static string GetCorrelationId(StringValues value)
    {
        var supplied = value.FirstOrDefault();
        return !string.IsNullOrWhiteSpace(supplied) && supplied.Length <= 128
            ? supplied
            : Guid.NewGuid().ToString("N");
    }
}
