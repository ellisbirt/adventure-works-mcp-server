using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnterpriseAiGateway.Data;

public enum McpFieldExposure
{
    Exclude = 0,
    Safe = 1,
    Redact = 2,
}

// Exposure is stored as an EF model annotation (rather than a separate hardcoded table/column
// map) so it lives on the same entity model that the scaffolder regenerates: a newly scaffolded
// column has no annotation and is excluded by default instead of silently becoming visible.
public static class McpEntityExposure
{
    public const string AnnotationName = "Mcp:FieldExposure";

    public static void Annotate<TEntity>(
        EntityTypeBuilder<TEntity> entity,
        McpFieldExposure exposure,
        params string[] propertyNames)
        where TEntity : class
    {
        foreach (var propertyName in propertyNames)
        {
            entity.Property(propertyName).HasAnnotation(AnnotationName, exposure.ToString());
        }
    }
}
