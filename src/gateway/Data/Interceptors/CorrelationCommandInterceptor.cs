using System.Data.Common;
using EnterpriseAiGateway.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EnterpriseAiGateway.Data.Interceptors;

public sealed class CorrelationCommandInterceptor : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        TagCommand(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        TagCommand(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private static void TagCommand(DbCommand command)
    {
        var correlationId = CorrelationContext.Id;
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            command.CommandText = $"/* CorrelationId: {Sanitize(correlationId)} */\n{command.CommandText}";
        }
    }

    private static string Sanitize(string value) => value.Replace("*/", "* /", StringComparison.Ordinal);
}
