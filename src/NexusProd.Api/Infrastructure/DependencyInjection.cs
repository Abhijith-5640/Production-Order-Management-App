using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.UseCases.Auth;
using NexusProd.Api.Application.UseCases.Config;
using NexusProd.Api.Infrastructure.Persistence;
using NexusProd.Api.Infrastructure.Security;
using NexusProd.Api.Infrastructure.Time;
using NexusProd.Api.Infrastructure.Configuration;
using NexusProd.Api.Updater;

namespace NexusProd.Api.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Settings
        var jwt = new JwtSettings();
        configuration.GetSection("JwtSettings").Bind(jwt);
        services.AddSingleton(jwt);

        var update = new UpdateServerSettings();
        configuration.GetSection("UpdateServerSettings").Bind(update);
        services.AddSingleton(update);

        // Time
        services.AddSingleton<IClock, SystemClock>();

        // Security
        services.AddSingleton<IPasswordHasher, Base64PasswordHasher>();
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<IJwtTokenService>(sp => sp.GetRequiredService<JwtTokenService>());
        services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
        services.AddSingleton<IAccessTokenBlacklist, InMemoryAccessTokenBlacklist>();

        // Persistence
        services.AddSingleton<MySqlConnectionFactory>();
        services.AddSingleton<IUnitOfWork, MySqlUnitOfWork>();
        services.AddSingleton<IDbConfigStore, FileDbConfigStore>();
        services.AddScoped<IUserRepository, MySqlUserRepository>();
        services.AddScoped<IOrderRepository, MySqlOrderRepository>();

        // Update triggers
        services.AddSingleton<InMemoryUpdateState>();
        services.AddSingleton<IUpdateState>(sp => sp.GetRequiredService<InMemoryUpdateState>());
        services.AddSingleton<InMemoryUpdateTrigger>();
        services.AddSingleton<IUpdateTrigger>(sp => sp.GetRequiredService<InMemoryUpdateTrigger>());
        services.AddSingleton<IUpdateInstaller, NullUpdateInstaller>();
        services.AddHttpClient<IUpdateServer, HttpUpdateServer>();

        return services;
    }
}

/// <summary>
/// Placeholder installer for the early bootstrap. The real
/// <c>FileSystemUpdateInstaller</c> is wired in the <c>Updater</c>
/// composition step in <c>Program.cs</c> after the install dir is known.
/// </summary>
internal sealed class NullUpdateInstaller : IUpdateInstaller
{
    public string GetCurrentVersion() => "1.0.0";
    public Task ApplyUpdateAsync(string zipPath, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class InMemoryUpdateState : IUpdateState
{
    private UpdateStatus _current = UpdateStatus.Initial;
    public UpdateStatus Current => _current;
    public void Set(UpdateStatus status) => _current = status;
}

public sealed class InMemoryUpdateTrigger : IUpdateTrigger
{
    public event Action? OnTrigger;
    public void RequestCheck() => OnTrigger?.Invoke();
}
