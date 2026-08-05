using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Application;

namespace Payments.Infrastructure;

/// <summary>
/// Saga phía Payments: nhận TransferCompleted / TransferFailed từ Accounts → đóng vòng đời transfer.
/// Nhờ vậy lệnh chuyển tiền không bao giờ kẹt ở "Initiated" — client biết được kết quả cuối.
/// </summary>
public sealed class TransferResultConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<ServiceBusOptions> options,
    ILogger<TransferResultConsumer> logger) : BackgroundService
{
    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        var subscription = string.IsNullOrWhiteSpace(opt.SubscriptionName)
            ? "payments-subscription"
            : opt.SubscriptionName;

        _client = new ServiceBusClient(opt.ConnectionString);
        _processor = _client.CreateProcessor(opt.TopicName, subscription,
            new ServiceBusProcessorOptions { MaxConcurrentCalls = 4, AutoCompleteMessages = false });

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "Service Bus error trên {Entity}", args.EntityPath);
            return Task.CompletedTask;
        };

        await _processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation("Payments consumer lắng nghe {Topic}/{Sub}", opt.TopicName, subscription);
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        CorrelationContext.Set(args.Message.CorrelationId ?? Guid.NewGuid().ToString("N"));
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationContext.LogPropertyName] = CorrelationContext.Id ?? "-",
        });

        // Schema lạ (publisher đã nâng cấp trước) thì dead-letter, không đoán mò trên dữ liệu tiền tệ.
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
            var writer = scope.ServiceProvider.GetRequiredService<ITransferStatusWriter>();
            var body = args.Message.Body.ToString();

            switch (args.Message.Subject)
            {
                case nameof(TransferCompletedIntegrationEvent):
                {
                    var e = JsonSerializer.Deserialize<TransferCompletedIntegrationEvent>(body)!;
                    await writer.MarkCompletedAsync(e.TransferId, args.CancellationToken);
                    logger.LogInformation("Transfer {TransferId} → Completed", e.TransferId);
                    break;
                }
                case nameof(TransferFailedIntegrationEvent):
                {
                    var e = JsonSerializer.Deserialize<TransferFailedIntegrationEvent>(body)!;
                    await writer.MarkFailedAsync(e.TransferId, e.ErrorCode, e.Reason, args.CancellationToken);
                    logger.LogWarning("Transfer {TransferId} → Failed ({Code})", e.TransferId, e.ErrorCode);
                    break;
                }
            }

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Xử lý result message {MessageId} lỗi — abandon", args.Message.MessageId);
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
