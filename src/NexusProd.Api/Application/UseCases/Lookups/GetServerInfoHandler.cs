using System.Net.NetworkInformation;
using System.Net.Sockets;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Lookups;

public sealed record GetServerInfoQuery();
public sealed record ServerInfoView(
    string Version,
    DateTimeOffset ServerTime,
    TimeSpan Uptime,
    IReadOnlyList<string> LanAddresses,
    int Port);

public sealed class GetServerInfoHandler : IHandler<GetServerInfoQuery, ServerInfoView>
{
    private readonly IClock _clock;
    private readonly IUpdateInstaller _installer;
    private static readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public GetServerInfoHandler(IClock clock, IUpdateInstaller installer)
    {
        _clock = clock;
        _installer = installer;
    }

    public Task<Result<ServerInfoView>> HandleAsync(GetServerInfoQuery request, CancellationToken cancellationToken)
    {
        var addresses = EnumerateLanAddresses();
        var info = new ServerInfoView(
            Version: _installer.GetCurrentVersion(),
            ServerTime: _clock.UtcNow,
            Uptime: _clock.UtcNow - _startedAt,
            LanAddresses: addresses,
            Port: 5000);
        return Task.FromResult(Result<ServerInfoView>.Success(info));
    }

    private static IReadOnlyList<string> EnumerateLanAddresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var props = ni.GetIPProperties();
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    list.Add(ua.Address.ToString());
                }
            }
        }
        catch
        {
            // Best-effort; return what we have.
        }
        return list;
    }
}
