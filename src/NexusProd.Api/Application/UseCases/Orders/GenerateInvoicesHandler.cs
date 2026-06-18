using Microsoft.Extensions.Logging;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Orders;

public sealed record GenerateInvoicesCommand(int UserId, int BrnchId, int UserCounterId);
public sealed record GenerateInvoicesResult(int InvoiceCount, string Message);

public sealed class GenerateInvoicesHandler : IHandler<GenerateInvoicesCommand, GenerateInvoicesResult>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<GenerateInvoicesHandler> _logger;

    public GenerateInvoicesHandler(IOrderRepository orders, ILogger<GenerateInvoicesHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result<GenerateInvoicesResult>> HandleAsync(GenerateInvoicesCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var count = await _orders.GenerateInvoicesAsync(request.UserId, request.BrnchId, request.UserCounterId, cancellationToken);
            var message = count == 0
                ? "No bills saved yet."
                : $"{count} invoices generated successfully.";
            return new GenerateInvoicesResult(count, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateInvoices failed for userId {UserId} brnchId {BrnchId} userCounterId {UserCounterId}", request.UserId, request.BrnchId, request.UserCounterId);
            return Error.DatabaseError("Invoice generation failed: " + ex.Message);
        }
    }
}
