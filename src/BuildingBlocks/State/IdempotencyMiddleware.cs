using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BuildingBlocks.State;

/// <summary>
/// Bắt header <c>Idempotency-Key</c> trên các request thay đổi dữ liệu (POST).
/// Lần đầu: xử lý và **cache lại response**. Lần sau cùng key: trả thẳng response cũ (kèm
/// <c>Idempotency-Replayed: true</c>) — không tạo giao dịch trùng.
/// Đang xử lý dở mà có request thứ hai → 409, tránh double-spend do double-click.
/// </summary>
public sealed class IdempotencyMiddleware(
    RequestDelegate next,
    IIdempotencyStore store,
    ILogger<IdempotencyMiddleware> logger)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!HttpMethods.IsPost(ctx.Request.Method)
            || !ctx.Request.Headers.TryGetValue("Idempotency-Key", out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            await next(ctx);
            return;
        }

        var key = $"{ctx.Request.Path}:{raw}";

        var cached = await store.GetResultAsync(key, ctx.RequestAborted);
        if (cached is not null)
        {
            logger.LogInformation("Idempotent replay cho key {Key}", raw.ToString());
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            ctx.Response.ContentType = "application/json";
            ctx.Response.Headers["Idempotency-Replayed"] = "true";
            await ctx.Response.WriteAsync(cached, ctx.RequestAborted);
            return;
        }

        if (!await store.TryBeginAsync(key, Ttl, ctx.RequestAborted))
        {
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            await ctx.Response.WriteAsync(
                """{"errorCode":"IDEMPOTENCY_IN_PROGRESS","detail":"Yêu cầu cùng Idempotency-Key đang được xử lý."}""",
                ctx.RequestAborted);
            return;
        }

        // Ghi lại body response để cache — chỉ cache khi thành công.
        var original = ctx.Response.Body;
        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;
        try
        {
            await next(ctx);

            buffer.Position = 0;
            var body = await new StreamReader(buffer).ReadToEndAsync(ctx.RequestAborted);
            buffer.Position = 0;
            await buffer.CopyToAsync(original, ctx.RequestAborted);

            if (ctx.Response.StatusCode is >= 200 and < 300)
                await store.SaveResultAsync(key, body, Ttl, ctx.RequestAborted);
            else
                await store.ReleaseAsync(key, ctx.RequestAborted);   // lỗi → cho retry ngay
        }
        catch
        {
            await store.ReleaseAsync(key, ctx.RequestAborted);
            throw;
        }
        finally
        {
            ctx.Response.Body = original;
        }
    }
}

public static class StateExtensions
{
    /// <summary>Đăng ký Redis state (idempotency + distributed lock); no-op nếu chưa cấu hình Redis.</summary>
    public static IServiceCollection AddRedisState(this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(conn))
        {
            services.AddSingleton<IIdempotencyStore, NoOpIdempotencyStore>();
            services.AddSingleton<IDistributedLock, NoOpDistributedLock>();
            services.AddSingleton<BuildingBlocks.Auth.ILoginThrottle, BuildingBlocks.Auth.NoOpLoginThrottle>();
            return services;
        }

        services.TryAddRedis(conn);
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        services.AddSingleton(new BuildingBlocks.Auth.LoginThrottleOptions());
        services.AddSingleton<BuildingBlocks.Auth.ILoginThrottle, BuildingBlocks.Auth.RedisLoginThrottle>();
        return services;
    }

    private static void TryAddRedis(this IServiceCollection services, string conn)
    {
        if (services.Any(d => d.ServiceType == typeof(IConnectionMultiplexer))) return;
        services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(conn));
    }

    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app)
        => app.UseMiddleware<IdempotencyMiddleware>();
}
