using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Infrastructure.Configuration;

namespace NexusProd.Api.Updater;

/// <summary>
/// Polls the remote update server's <c>/version.json</c> endpoint. The
/// response shape is { latestVersion, releaseDate, downloadUrl, releaseNotes }.
/// </summary>
public sealed class HttpUpdateServer : IUpdateServer
{
    private readonly HttpClient _http;
    private readonly UpdateServerSettings _settings;
    private readonly ILogger<HttpUpdateServer> _logger;

    public HttpUpdateServer(HttpClient http, IOptions<UpdateServerSettings> settings, ILogger<HttpUpdateServer> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
        _http.Timeout = TimeSpan.FromMinutes(2);
    }

    public async Task<RemoteVersionInfo?> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_settings.Url.TrimEnd('/')}/version.json";
            var info = await _http.GetFromJsonAsync<RemoteVersionInfo>(url, cancellationToken);
            return info;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed against {Url}", _settings.Url);
            return null;
        }
    }

    public async Task<string> DownloadPackageAsync(string downloadUrl, string destinationPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(destinationPath);
        await stream.CopyToAsync(file, cancellationToken);
        return destinationPath;
    }
}
