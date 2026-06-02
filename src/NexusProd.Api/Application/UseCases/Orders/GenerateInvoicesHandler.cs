using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Orders;

public sealed record GenerateInvoicesCommand(int UserId);
public sealed record GenerateInvoicesResult(int InvoiceCount, string Message);

public sealed class GenerateInvoicesHandler : IHandler<GenerateInvoicesCommand, GenerateInvoicesResult>
{
    private readonly IOrderRepository _orders;

    public GenerateInvoicesHandler(IOrderRepository orders) => _orders = orders;

    public async Task<Result<GenerateInvoicesResult>> HandleAsync(GenerateInvoicesCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var count = await _orders.GenerateInvoicesAsync(request.UserId, cancellationToken);
            var message = count == 0
                ? "No pending orders to generate"
                : $"{count} invoices generated successfully.";
            return new GenerateInvoicesResult(count, message);
        }
        catch (Exception ex)
        {
            return Error.DatabaseError("Invoice generation failed: " + ex.Message);
        }
    }
}
