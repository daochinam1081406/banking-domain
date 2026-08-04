using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Http;

/// <summary>
/// Map exception có property `ErrorCode` (domain exception của mọi service) → 400 ProblemDetails.
/// Dùng reflection để BuildingBlocks không phải phụ thuộc ngược vào Domain của từng service.
/// Lỗi ngoài dự kiến → 500 (không lộ stack trace ra client).
/// </summary>
public sealed class DomainExceptionMiddleware(RequestDelegate next, ILogger<DomainExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (Exception ex)
        {
            var errorCode = ex.GetType().GetProperty("ErrorCode")?.GetValue(ex) as string;

            if (errorCode is not null)
            {
                logger.LogWarning("Domain error {ErrorCode}: {Message}", errorCode, ex.Message);
                await WriteAsync(ctx, HttpStatusCode.BadRequest, errorCode, ex.Message);
                return;
            }

            logger.LogError(ex, "Unhandled exception");
            await WriteAsync(ctx, HttpStatusCode.InternalServerError, "INTERNAL_ERROR",
                "Đã có lỗi xảy ra, vui lòng thử lại.");
        }
    }

    private static async Task WriteAsync(HttpContext ctx, HttpStatusCode status, string code, string detail)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.Clear();
        ctx.Response.StatusCode = (int)status;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "about:blank",
            title = status == HttpStatusCode.BadRequest ? "Yêu cầu không hợp lệ" : "Lỗi hệ thống",
            status = (int)status,
            errorCode = code,
            detail,
        }));
    }
}

public static class DomainExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseDomainExceptionHandler(this IApplicationBuilder app)
        => app.UseMiddleware<DomainExceptionMiddleware>();
}
