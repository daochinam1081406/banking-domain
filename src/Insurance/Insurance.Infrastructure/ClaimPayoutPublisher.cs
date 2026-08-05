using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using Insurance.Application;

namespace Insurance.Infrastructure;

/// <summary>Phát ClaimApproved lên broker để Payments chi trả.</summary>
public sealed class ClaimPayoutPublisher(IEventBus bus) : IClaimPayoutPublisher
{
    public Task PublishApprovedAsync(ClaimApprovedIntegrationEvent @event, CancellationToken ct = default)
        => bus.PublishAsync(@event, ct);

    public Task PublishPremiumDueAsync(PremiumDueIntegrationEvent @event, CancellationToken ct = default)
        => bus.PublishAsync(@event, ct);
}
