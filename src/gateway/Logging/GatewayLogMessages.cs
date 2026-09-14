using Microsoft.Extensions.Logging;

namespace EnterpriseAiGateway.Logging;

internal static partial class GatewayLogMessages
{
    [LoggerMessage(LogLevel.Information, "MCP tools metadata requested")]
    public static partial void ToolsRequested(ILogger logger);

    [LoggerMessage(LogLevel.Warning, "MCP tool request rejected because tool {ToolName} is not mapped")]
    public static partial void UnknownTool(ILogger logger, string toolName);

    [LoggerMessage(LogLevel.Warning, "MCP tool request rejected because customerId is missing or malformed")]
    public static partial void InvalidCustomerId(ILogger logger);

    [LoggerMessage(LogLevel.Information, "MCP customer history requested for customer {CustomerId}")]
    public static partial void CustomerHistoryRequested(ILogger logger, int customerId);
}