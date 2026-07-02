using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Api.Endpoints;

/// <summary>
/// Maps a <see cref="Result{T}"/> to an <c>IResult</c>. Shared by every
/// endpoint so the HTTP response shapes stay consistent.
/// </summary>
internal static class ResultExtensions
{
    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        if (result.IsSuccess) return onSuccess(result.Value!);
        return ToHttpError(result.Error);
    }

    public static Task<IResult> ToHttpAsync<T>(this Result<T> result, Func<T, Task<IResult>> onSuccess)
    {
        if (result.IsSuccess) return onSuccess(result.Value!);
        return Task.FromResult(ToHttpError(result.Error));
    }

    private static IResult ToHttpError(Error error) => error.Code switch
    {
        ErrorCode.Unauthorized => Results.Json(new { success = false, error = error.Code.ToString().ToLowerInvariant(), message = error.Message }, statusCode: StatusCodes.Status401Unauthorized),
        ErrorCode.Forbidden => Results.Json(new { success = false, error = error.Code.ToString().ToLowerInvariant(), message = error.Message }, statusCode: StatusCodes.Status403Forbidden),
        ErrorCode.NotFound => Results.Json(new { success = false, error = error.Code.ToString().ToLowerInvariant(), message = error.Message }, statusCode: StatusCodes.Status404NotFound),
        ErrorCode.InvalidInput => Results.Json(new { success = false, error = error.Code.ToString().ToLowerInvariant(), message = error.Message }, statusCode: StatusCodes.Status400BadRequest),
        ErrorCode.Conflict => Results.Json(new { success = false, error = error.Code.ToString().ToLowerInvariant(), message = error.Message }, statusCode: StatusCodes.Status409Conflict),
        ErrorCode.DatabaseError or ErrorCode.ConfigurationError =>
            Results.Json(new { success = false, error = error.Code.ToString().ToLowerInvariant(), message = error.Message }, statusCode: StatusCodes.Status500InternalServerError),
        _ => Results.Json(new { success = false, error = error.Code.ToString().ToLowerInvariant(), message = error.Message }, statusCode: StatusCodes.Status500InternalServerError)
    };
}
