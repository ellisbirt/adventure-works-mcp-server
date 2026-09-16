using System.Text;
using EnterpriseAiGateway.Data.Interceptors;
using EnterpriseAiGateway.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnterpriseAiGateway.Tests.Infrastructure;

public class CorrelationTests
{
    [Fact]
    public async Task Middleware_PreservesValidRequestIdAndResponseHeader()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationMiddleware.HeaderName] = "request-123";
        var middleware = new CorrelationMiddleware(
            next: _ =>
            {
                CorrelationContext.Id.Should().Be("request-123");
                return Task.CompletedTask;
            },
            NullLogger<CorrelationMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Headers[CorrelationMiddleware.HeaderName].ToString().Should().Be("request-123");
        CorrelationContext.Id.Should().BeNull();
    }

    [Fact]
    public async Task Middleware_ReplacesOversizedRequestIdAndClearsContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationMiddleware.HeaderName] = new string('x', 129);
        string? observedId = null;
        var middleware = new CorrelationMiddleware(
            next: _ =>
            {
                observedId = CorrelationContext.Id;
                return Task.CompletedTask;
            },
            NullLogger<CorrelationMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        observedId.Should().NotBeNullOrWhiteSpace().And.HaveLength(32);
        context.Response.Headers[CorrelationMiddleware.HeaderName].ToString().Should().Be(observedId);
        CorrelationContext.Id.Should().BeNull();
    }

    [Fact]
    public void CommandInterceptor_SanitizesCommentTerminators()
    {
        CorrelationCommandInterceptor.Sanitize("safe*/unsafe").Should().Be("safe* /unsafe");
    }
}