using System.Diagnostics;

namespace EnterpriseAiGateway.Infrastructure;

public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> CurrentId = new();

    public static string? Id
    {
        get => CurrentId.Value ?? Activity.Current?.TraceId.ToString();
        set => CurrentId.Value = value;
    }
}
