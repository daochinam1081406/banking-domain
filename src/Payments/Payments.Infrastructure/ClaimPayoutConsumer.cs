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
using Payments.Domain;
using Payments.Domain.Exceptions;

namespace Payments.Infrastructure;

public sealed class InsurerAccountOptions
{
    /// <summary>Tài khoản quỹ của công ty bảo hiểm — nguồn tiền chi trả bồi thường.</summary>
    public string PayoutFromAccount { get; set; } = "INS-FUND";

    /// <summary>
    /// Subscription RIÊNG cho luồng chi trả. Dùng chung với TransferResultConsumer sẽ thành
    /// competing consumers trên cùng subscription → message bị consumer kia Complete và mất.
    /// </summary>
    public string SubscriptionName { get; set; } = "payments-payout-subscription";
}

/// <summary>
/// Saga bảo hiểm → ngân hàng: nhận `ClaimApproved` từ Insurance → tạo lệnh chuyển tiền từ quỹ
/// bảo hiểm sang tài khoản khách, rồi báo kết quả về (`ClaimPayoutCompleted` / `ClaimPayoutFailed`).
/// Đây là chỗ hai domain gặp nhau: hồ sơ bồi thường trở thành dòng tiền thật.
/// </summary>
public sealed class ClaimPayoutConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<ServiceBusOptions> options,
    IOptions<InsurerAccountOptions> insurer,
    ILogger<ClaimPayoutConsumer> logger) : BackgroundService
{
    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        _client = new ServiceBusClient(opt.ConnectionString);
        _processor = _client.CreateProcessor(opt.TopicName, insurer.Value.SubscriptionName,
            new ServiceBusProcessorOptions { MaxConcurrentCalls = 2, AutoCompleteMessages = false });

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "Service Bus error trên {Entity}", args.EntityPath);
            return Task.CompletedTask;
        };

        await _processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation("Payments payout consumer lắng nghe {Topic}/{Sub}",
            opt.TopicName, insurer.Value.SubscriptionName);
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var subject = args.Message.Subject;
        if (subject != nameof(ClaimApprovedIntegrationEvent) && subject != nameof(PremiumDueIntegrationEvent))
        {
            await args.CompleteMessageAsync(args.Message);
            return;
        }

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
            var repo = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
            var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

            if (subject == nameof(PremiumDueIntegrationEvent))
            {
                await CollectPremiumAsync(args, repo, bus);
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            var e = JsonSerializer.Deserialize<ClaimApprovedIntegrationEvent>(args.Message.Body.ToString())!;
            try
            {
                // Chi trả: quỹ bảo hiểm → tài khoản khách. Dùng chính luồng Outbox của Payments.
                var transfer = Transfer.Initiate(
                    insurer.Value.PayoutFromAccount, e.PayoutAccount, e.Amount, e.Currency);

                await repo.SaveWithOutboxAsync(transfer, new MoneyTransferredIntegrationEvent
                {
                    TransferId  = transfer.Id,
                    FromAccount = transfer.FromAccount,
                    ToAccount   = transfer.ToAccount,
                    Amount      = transfer.Amount,
                    Currency    = transfer.Currency,
                }, args.CancellationToken);

                await bus.PublishAsync(new ClaimPayoutCompletedIntegrationEvent
                {
                    ClaimId = e.ClaimId,
                    TransferId = transfer.Id,
                }, args.CancellationToken);

                logger.LogInformation(
                    "Chi trả bồi thường {ClaimNumber}: {Amount} {Currency} → {Account} (transfer {TransferId})",
                    e.ClaimNumber, e.Amount, e.Currency, e.PayoutAccount, transfer.Id);
            }
            catch (PaymentsDomainException ex)
            {
                // Lỗi nghiệp vụ (permanent) → báo Insurance để xử lý lại, không retry vô ích.
                await bus.PublishAsync(new ClaimPayoutFailedIntegrationEvent
                {
                    ClaimId = e.ClaimId,
                    Reason = ex.Message,
                }, args.CancellationToken);
                logger.LogWarning("Chi trả {ClaimNumber} thất bại: {Reason}", e.ClaimNumber, ex.Message);
            }

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Xử lý ClaimApproved {MessageId} lỗi hạ tầng — abandon", args.Message.MessageId);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    /// <summary>
    /// Bancassurance — thu phí: trích nợ tài khoản khách → quỹ bảo hiểm. Không đủ số dư thì
    /// báo thất bại để hợp đồng giữ nguyên Draft (không cho hiệu lực khi chưa có tiền).
    /// </summary>
    private async Task CollectPremiumAsync(
        ProcessMessageEventArgs args, ITransferRepository repo, IEventBus bus)
    {
        var e = JsonSerializer.Deserialize<PremiumDueIntegrationEvent>(args.Message.Body.ToString())!;
        try
        {
            var transfer = Transfer.Initiate(
                e.DebitAccount, insurer.Value.PayoutFromAccount, e.Amount, e.Currency);

            await repo.SaveWithOutboxAsync(transfer, new MoneyTransferredIntegrationEvent
            {
                TransferId  = transfer.Id,
                FromAccount = transfer.FromAccount,
                ToAccount   = transfer.ToAccount,
                Amount      = transfer.Amount,
                Currency    = transfer.Currency,
            }, args.CancellationToken);

            await bus.PublishAsync(new PremiumCollectedIntegrationEvent
            {
                PolicyId = e.PolicyId,
                TransferId = transfer.Id,
            }, args.CancellationToken);

            logger.LogInformation("Thu phí {PolicyNumber}: {Amount} {Currency} từ {Account}",
                e.PolicyNumber, e.Amount, e.Currency, e.DebitAccount);
        }
        catch (PaymentsDomainException ex)
        {
            await bus.PublishAsync(new PremiumCollectionFailedIntegrationEvent
            {
                PolicyId = e.PolicyId,
                Reason = ex.Message,
            }, args.CancellationToken);
            logger.LogWarning("Thu phí {PolicyNumber} thất bại: {Reason}", e.PolicyNumber, ex.Message);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null) await _processor.DisposeAsync();
        if (_client is not null) await _client.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }
}
