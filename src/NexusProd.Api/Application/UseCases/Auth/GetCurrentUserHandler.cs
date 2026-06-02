using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Auth;

public sealed record GetCurrentUserQuery(int UserId);
public sealed record CurrentUserView(int UserId, string UserName);

/// <summary>
/// Returns the user view behind the access token. The token is already
/// validated by the JWT middleware; the handler just projects the
/// relevant claims into a DTO.
/// </summary>
public sealed class GetCurrentUserHandler : IHandler<GetCurrentUserQuery, CurrentUserView>
{
    public Task<Result<CurrentUserView>> HandleAsync(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        // The API layer reads `name` and `sub` from the JWT principal
        // directly; the handler exists so future server-side enrichment
        // (e.g. fetching the user's branches) has a home.
        return Task.FromResult(Result<CurrentUserView>.Success(new CurrentUserView(request.UserId, string.Empty)));
    }
}
