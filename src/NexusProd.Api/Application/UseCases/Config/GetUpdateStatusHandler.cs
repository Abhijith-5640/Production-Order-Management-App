using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Config;

public sealed record GetUpdateStatusQuery();
public sealed record GetUpdateStatusResult(UpdateStatus Status);

public sealed class GetUpdateStatusHandler : IHandler<GetUpdateStatusQuery, GetUpdateStatusResult>
{
    private readonly IUpdateState _state;

    public GetUpdateStatusHandler(IUpdateState state) => _state = state;

    public Task<Result<GetUpdateStatusResult>> HandleAsync(GetUpdateStatusQuery request, CancellationToken cancellationToken)
        => Task.FromResult(Result<GetUpdateStatusResult>.Success(new GetUpdateStatusResult(_state.Current)));
}
