using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using Insurance.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Insurance.Infrastructure;

/// <summary>
/// Saga phía Insurance: Payments chi trả xong → đóng hồ sơ (Paid); thất bại → ghi nhận để xử lý lại.
/// `MarkPaid` idempotent nên message lặp không gây lỗi.
/// </summary>
public sealed class ClaimPayoutResultConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<ServiceBusOptions> options,
    ILogger<ClaimPayoutResultConsumer> logger) : BackgroundService
{
    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        _client = new ServiceBusClient(opt.ConnectionString);
        _processor = _client.CreateProcessor(opt.TopicName, opt.SubscriptionName,
            new ServiceBusProcessorOptions { MaxConcurrentCalls = 4, AutoCompleteMessages = false });

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "Service Bus error trên {Entity}", args.EntityPath);
            return Task.CompletedTask;
        };

        await _processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation("Insurance consumer lắng nghe {Topic}/{Sub}", opt.TopicName, opt.SubscriptionName);
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        CorrelationContext.Set(args.Message.CorrelationId ?? Guid.NewGuid().ToString("N"));
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationContext.LogPropertyName] = CorrelationContext.Id ?? "-",
        });


        // Contract versioning: schema lạ (do publisher đã nâng cấp trước) thì DỪNG, không đoán mò —
        // đoán sai trên dữ liệu tiền bạc tệ hơn nhiều so với việc dead-letter và cảnh báo.
        var schemaVersion = args.Message.ApplicationProperties
            .TryGetValue(SchemaCompatibility.MessagePropertyName, out var sv) && sv is not null
                ? Convert.ToInt32(sv) : 1;
        if (!SchemaCompatibility.IsSupported(schemaVersion))
        {
            logger.LogError("Message {MessageId} dùng schema v{Version} — dead-letter",
                args.Message.MessageId, schemaVersion);
            await args.DeadLetterMessageAsync(args.Message, "UnsupportedSchemaVersion",
                SchemaCompatibility.Reason(schemaVersion));
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var claims = scope.ServiceProvider.GetRequiredService<IClaimRepository>();
            var policies = scope.ServiceProvider.GetRequiredService<IPolicyRepository>();
            var body = args.Message.Body.ToString();

            switch (args.Message.Subject)
            {
                case nameof(ClaimPayoutCompletedIntegrationEvent):
                {
                    var e = JsonSerializer.Deserialize<ClaimPayoutCompletedIntegrationEvent>(body)!;
                    var claim = await claims.GetByIdAsync(e.ClaimId, args.CancellationToken);
                    if (claim is not null)
                    {
                        claim.MarkPaid(e.TransferId);
                        await claims.SaveChangesAsync(args.CancellationToken);
                        logger.LogInformation("Claim {ClaimId} → Paid (transfer {TransferId})", e.ClaimId, e.TransferId);
                    }
                    break;
                }
                case nameof(PremiumCollectedIntegrationEvent):
                {
                    var e = JsonSerializer.Deserialize<PremiumCollectedIntegrationEvent>(body)!;
                    var policy = await policies.GetByIdAsync(e.PolicyId, args.CancellationToken);
                    if (policy is not null)
                    {
                        policy.MarkPremiumCollected(e.TransferId);   // idempotent
                        await policies.SaveChangesAsync(args.CancellationToken);
                        logger.LogInformation("Đã thu phí hợp đồng {PolicyId} → Active", e.PolicyId);
                    }
                    break;
                }
                case nameof(PremiumCollectionFailedIntegrationEvent):
                {
                    var e = JsonSerializer.Deserialize<PremiumCollectionFailedIntegrationEvent>(body)!;
                    logger.LogWarning("Thu phí hợp đồng {PolicyId} thất bại: {Reason} — giữ Draft",
                        e.PolicyId, e.Reason);
                    break;
                }
                case nameof(ClaimPayoutFailedIntegrationEvent):
                {
                    var e = JsonSerializer.Deserialize<ClaimPayoutFailedIntegrationEvent>(body)!;
                    var claim = await claims.GetByIdAsync(e.ClaimId, args.CancellationToken);
                    if (claim is not null)
                    {
                        claim.MarkPayoutFailed(e.Reason);
                        await claims.SaveChangesAsync(args.CancellationToken);
                        logger.LogWarning("Claim {ClaimId} chi trả thất bại: {Reason}", e.ClaimId, e.Reason);
                    }
                    break;
                }
            }

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Xử lý message {MessageId} lỗi — abandon", args.Message.MessageId);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null) await _processor.DisposeAsync();
        if (_client is not null) await _client.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
