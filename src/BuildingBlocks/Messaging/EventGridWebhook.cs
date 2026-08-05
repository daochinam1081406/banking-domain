using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Endpoint nhận event từ Azure Event Grid. Khác Service Bus/Kafka ở chỗ Event Grid là push:
/// Azure gọi HTTP vào webhook của mình, không phải mình pull.
///
/// Bắt buộc xử lý SubscriptionValidationEvent: khi tạo subscription, Event Grid gửi 1 event đặc biệt
/// kèm `validationCode`, webhook phải echo lại `validationResponse` — không làm thì subscription không
/// bao giờ active. Đây là chi tiết hay bị hỏi khi phỏng vấn về Event Grid.
/// </summary>
public static class EventGridWebhook
{
    private const string ValidationEventType = "Microsoft.EventGrid.SubscriptionValidationEvent";

    public static IEndpointRouteBuilder MapEventGridWebhook(
        this IEndpointRouteBuilder app,
        string route,
        Func<string, string, CancellationToken, Task> handler)
    {
        app.MapPost(route, async (HttpContext ctx, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("EventGridWebhook");
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct);

            // Event Grid luôn gửi mảng event.
            var events = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToList()
                : [doc.RootElement];

            foreach (var e in events)
            {
                var eventType = e.TryGetProperty("eventType", out var t) ? t.GetString() ?? "" : "";

                // Handshake: echo validationCode để Azure kích hoạt subscription.
                if (eventType == ValidationEventType)
                {
                    var code = e.GetProperty("data").GetProperty("validationCode").GetString();
                    logger.LogInformation("Event Grid subscription validation handshake");
                    return Results.Ok(new { validationResponse = code });
                }

                var payload = e.TryGetProperty("data", out var d) ? d.GetRawText() : "{}";
                await handler(eventType, payload, ct);
                logger.LogInformation("Event Grid nhận {EventType}", eventType);
            }

            return Results.Ok();
        }).AllowAnonymous();   // Azure gọi vào — bảo vệ bằng shared secret/AAD ở production

        return app;
    }
}
