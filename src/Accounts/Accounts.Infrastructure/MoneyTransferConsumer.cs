using System.Text.Json;
using Accounts.Application;
using Azure.Messaging.ServiceBus;
using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Accounts.Infrastructure;

/// <summary>
/// Consume MoneyTransferred → áp số dư (SQL Server) một cách **idempotent** (inbox theo MessageId).
/// Sau khi áp, phát event kết quả về Payments (saga): TransferCompleted | TransferFailed.
///
/// Phân loại lỗi:
///  - Lỗi nghiệp vụ (permanent, vd INSUFFICIENT_FUNDS) → phát TransferFailed + Complete message
///    (retry không bao giờ thành công, giữ lại chỉ tốn DLQ).
///  - Xung đột concurrency (transient) → retry trong tiến trình, hết lượt thì Abandon để broker giao lại.
///  - Lỗi hạ tầng → Abandon → retry → quá MaxDeliveryCount → dead-letter.
/// </summary>
public sealed class MoneyTransferConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<ServiceBusOptions> options,
    ILogger<MoneyTransferConsumer> logger) : BackgroundService
{
    private const int ConcurrencyRetries = 3;

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
        logger.LogInformation("Accounts consumer lắng nghe {Topic}/{Sub}", opt.TopicName, opt.SubscriptionName);
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        // Nối trace với service gửi: log của Accounts dùng chung CorrelationId với Payments.
        RestoreCorrelation(args.Message);
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

        if (args.Message.Subject != nameof(MoneyTransferredIntegrationEvent))
        {
            await args.CompleteMessageAsync(args.Message);   // event không quan tâm
            return;
        }

        MoneyTransferredIntegrationEvent? e;
        try
        {
            e = JsonSerializer.Deserialize<MoneyTransferredIntegrationEvent>(args.Message.Body.ToString());
        }
        catch (JsonException ex)
        {
            // Payload hỏng — retry vô nghĩa, đẩy thẳng dead-letter để điều tra.
            logger.LogError(ex, "Payload không hợp lệ, dead-letter {MessageId}", args.Message.MessageId);
            await args.DeadLetterMessageAsync(args.Message, "InvalidPayload", ex.Message);
            return;
        }

        if (e is null)
        {
            await args.DeadLetterMessageAsync(args.Message, "EmptyPayload", "Deserialize trả null");
            return;
        }

        try
        {
            var outcome = await ApplyWithConcurrencyRetryAsync(args.Message.MessageId, e, args.CancellationToken);

            if (outcome.Duplicate)
            {
                logger.LogInformation("Bỏ qua message trùng {MessageId} (transfer {TransferId})",
                    args.Message.MessageId, e.TransferId);
            }
            else if (outcome.Applied)
            {
                await PublishResultAsync(new TransferCompletedIntegrationEvent { TransferId = e.TransferId },
                    args.CancellationToken);
                logger.LogInformation("Applied transfer {TransferId}: {From} → {To} {Amount} {Currency}",
                    e.TransferId, e.FromAccount, e.ToAccount, e.Amount, e.Currency);
            }
            else
            {
                // Lỗi nghiệp vụ permanent → compensating event, không retry.
                await PublishResultAsync(new TransferFailedIntegrationEvent
                {
                    TransferId = e.TransferId,
                    ErrorCode = outcome.ErrorCode!,
                    Reason = outcome.Reason!,
                }, args.CancellationToken);
                logger.LogWarning("Transfer {TransferId} thất bại ({Code}): {Reason}",
                    e.TransferId, outcome.ErrorCode, outcome.Reason);
            }

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Xử lý message {MessageId} lỗi hạ tầng — abandon để retry", args.Message.MessageId);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private async Task<ApplyTransferOutcome> ApplyWithConcurrencyRetryAsync(
        string messageId, MoneyTransferredIntegrationEvent e, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var applier = scope.ServiceProvider.GetRequiredService<MoneyTransferApplier>();
            try
            {
                return await applier.ApplyAsync(
                    messageId, e.TransferId, e.FromAccount, e.ToAccount, e.Amount, e.Currency, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < ConcurrencyRetries)
            {
                // Account bị sửa song song — đọc lại state mới và thử lại (optimistic concurrency).
                logger.LogWarning("Concurrency conflict transfer {TransferId}, thử lại lần {Attempt}",
                    e.TransferId, attempt + 1);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), ct);
            }
            catch (DbUpdateException ex) when (IsDuplicateInbox(ex))
            {
                // Message trùng xử lý song song — cái kia đã ghi inbox trước.
                return ApplyTransferOutcome.AlreadyApplied();
            }
        }
    }

    private static void RestoreCorrelation(ServiceBusReceivedMessage message)
    {
        var correlationId = message.CorrelationId;

        if (string.IsNullOrWhiteSpace(correlationId)
            && message.ApplicationProperties.TryGetValue(CorrelationContext.MessagePropertyName, out var prop))
        {
            correlationId = prop?.ToString();
        }

        CorrelationContext.Set(string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId);
    }

    private static bool IsDuplicateInbox(DbUpdateException ex)
        => ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) == true;

    private async Task PublishResultAsync(IntegrationEvent @event, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        await bus.PublishAsync(@event, ct);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null) await _processor.DisposeAsync();
        if (_client is not null) await _client.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
