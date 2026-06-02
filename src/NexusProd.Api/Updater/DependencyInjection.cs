using Microsoft.Extensions.DependencyInjection;
using NexusProd.Api.Application.Abstractions;

namespace NexusProd.Api.Updater;

public static class DependencyInjection
{
    public static IServiceCollection AddUpdater(this IServiceCollection services)
    {
        // Replace the placeholder installer with the real one.
        services.AddSingleton<IUpdateInstaller, FileSystemUpdateInstaller>();
        services.AddHostedService<AppUpdater>();
        return services;
    }
}
