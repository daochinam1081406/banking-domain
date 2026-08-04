using System.Text.Json;
using Azure.Messaging.ServiceBus;
using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.Options;

namespace Accounts.Api;

/// <summary>
/// Consume message từ Azure Service Bus subscription → cập nhật số dư.
/// MessageId dùng cho idempotency; CompleteMessage sau khi xử lý thành công,
/// lỗi → AbandonMessage để retry, quá số lần → dead-letter queue (Service Bus tự động).
/// </summary>
public sealed class MoneyTransferConsumer(
    IOptions<ServiceBusOptions> options,
    AccountStore store,
    ILogger<MoneyTransferConsumer> logger) : BackgroundService
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
        logger.LogInformation("Accounts consumer đang lắng nghe {Topic}/{Sub}", opt.TopicName, opt.SubscriptionName);
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            if (args.Message.Subject == nameof(MoneyTransferredIntegrationEvent))
            {
                var e = JsonSerializer.Deserialize<MoneyTransferredIntegrationEvent>(
                    args.Message.Body.ToString());
                if (e is not null)
                {
                    store.Apply(e.FromAccount, e.ToAccount, e.Amount);
                    logger.LogInformation(
                        "Applied transfer {TransferId}: {From} → {To} {Amount} {Currency}",
                        e.TransferId, e.FromAccount, e.ToAccount, e.Amount, e.Currency);
                }
            }
            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Xử lý message {MessageId} thất bại — abandon để retry", args.Message.MessageId);
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
