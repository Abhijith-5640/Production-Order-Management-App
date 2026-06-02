namespace NexusProd.Api.Application.Abstractions;

/// <summary>
/// Talks to the remote update server. The HTTP implementation lives in
/// <c>Infrastructure/HttpUpdateServer</c>.
/// </summary>
public interface IUpdateServer
{
    /// <summary>Returns the latest published version, or null on any failure.</summary>
    Task<RemoteVersionInfo?> GetLatestVersionAsync(CancellationToken cancellationToken);

    /// <summary>Downloads the update package into <c>update-pending.zip</c>.</summary>
    Task<string> DownloadPackageAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken);
}

public sealed record RemoteVersionInfo(string LatestVersion, DateOnly ReleaseDate, string DownloadUrl, string? ReleaseNotes);
