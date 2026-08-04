using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace BuildingBlocks.Observability;

public static class ObservabilitySetup
{
    /// <summary>
    /// Structured logging (Serilog) + distributed tracing (OpenTelemetry).
    /// - Log kèm CorrelationId → grep 1 id ra được toàn bộ hành trình qua 2 service.
    /// - Trace tự động: ASP.NET Core request, HttpClient/gRPC outbound.
    /// - Exporter OTLP chỉ bật khi có `OTEL_EXPORTER_OTLP_ENDPOINT` (local không cần collector).
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Host.UseSerilog((ctx, cfg) => cfg
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", serviceName)
            .ReadFrom.Configuration(ctx.Configuration)
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] [{service}] [corr:{CorrelationId}] {Message:lj}{NewLine}{Exception}"));

        var tracing = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation();
                t.AddHttpClientInstrumentation();
            });

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            tracing.WithTracing(t => t.AddOtlpExporter());

        return builder;
    }

    /// <summary>Correlation ID + request logging — gọi SỚM trong pipeline.</summary>
    public static IApplicationBuilder UseObservability(this IApplicationBuilder app)
    {
        app.UseCorrelationId();
        app.UseSerilogRequestLogging();
        return app;
    }
}
