using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Observability;

/// <summary>
/// Nhận X-Correlation-Id từ client/gateway (hoặc sinh mới), đưa vào log scope + trả về response
/// để client trace được. Mọi log trong request sẽ tự kèm CorrelationId.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var correlationId = ctx.Request.Headers[CorrelationContext.HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = Guid.NewGuid().ToString("N");

        CorrelationContext.Set(correlationId);
        ctx.Response.Headers[CorrelationContext.HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationContext.LogPropertyName] = correlationId,
        }))
        {
            await next(ctx);
        }
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();
}
