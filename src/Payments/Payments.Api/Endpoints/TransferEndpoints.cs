using System.Security.Claims;
using Payments.Application;
using Payments.Domain.Exceptions;

namespace Payments.Api.Endpoints;

/// <summary>Chuyển tiền. POST nhận <c>Idempotency-Key</c> để client retry không tạo giao dịch trùng.</summary>
public static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/transfers").WithTags("Transfers").RequireAuthorization();

        g.MapPost("/", Create).WithName("CreateTransfer");
        g.MapGet("/{id:guid}", GetById).WithName("GetTransfer");

        return app;
    }

    private static async Task<IResult> Create(
        InitiateTransferCommand cmd, ClaimsPrincipal user,
        InitiateTransferHandler handler, CancellationToken ct)
    {
        try
        {
            // Người gọi lấy từ JWT, không tin client tự khai.
            var subject = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value ?? "";
            var result = await handler.HandleAsync(cmd with { RequestedBy = subject }, ct);
            return Results.Accepted($"/api/transfers/{result.TransferId}", result);
        }
        catch (PaymentsDomainException ex)
        {
            return Results.BadRequest(new { error = ex.ErrorCode, message = ex.Message });
        }
    }

    private static async Task<IResult> GetById(Guid id, ITransferReadService reads, CancellationToken ct)
    {
        var dto = await reads.GetByIdAsync(id, ct);
        return dto is null ? Results.NotFound() : Results.Ok(dto);
    }
}
