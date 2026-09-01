using Microsoft.Extensions.DependencyInjection;
using NexusProd.Api.Api.Contracts;
using NexusProd.Api.Application.Common;
using NexusProd.Api.Application.UseCases.Auth;
using NexusProd.Api.Application.UseCases.Config;
using NexusProd.Api.Application.UseCases.Lookups;
using NexusProd.Api.Application.UseCases.Orders;

namespace NexusProd.Api.Application;

/// <summary>
/// Wires every use case handler into DI. Single registration point so
/// the API project doesn't have to know which handler lives where.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Auth
        services.AddScoped<IHandler<LoginCommand, LoginResult>, LoginHandler>();
        services.AddScoped<IHandler<RefreshCommand, RefreshResult>, RefreshHandler>();
        services.AddScoped<IHandler<LogoutCommand, bool>, LogoutHandler>();
        services.AddScoped<IHandler<GetCurrentUserQuery, CurrentUserView>, GetCurrentUserHandler>();

        // Orders
        services.AddScoped<IHandler<CheckPendingQuery, CheckPendingResult>, CheckPendingHandler>();
        services.AddScoped<IHandler<GenerateInvoicesCommand, GenerateInvoicesResult>, GenerateInvoicesHandler>();
        services.AddScoped<IHandler<GetTariffViolationsQuery, TariffViolationResponse>, GetTariffViolationsHandler>();
        services.AddScoped<IHandler<GetOrdersQuery, GetOrdersResult>, GetOrdersHandler>();
        services.AddScoped<IHandler<UpdateInvoiceCommand, string>, UpdateInvoiceHandler>();
        services.AddScoped<IHandler<ExcludeItemCommand, string>, ExcludeItemHandler>();

        // Lookups
        services.AddScoped<IHandler<GetSectionsQuery, GetSectionsResult>, GetSectionsHandler>();
        services.AddScoped<IHandler<GetTripsQuery, GetTripsResult>, GetTripsHandler>();
        services.AddScoped<IHandler<GetServerInfoQuery, ServerInfoView>, GetServerInfoHandler>();

        // Config
        services.AddScoped<IHandler<GetConfigStatusQuery, GetConfigStatusResult>, GetConfigStatusHandler>();
        services.AddScoped<IHandler<SaveConfigCommand, string>, SaveConfigHandler>();
        services.AddScoped<IHandler<TestDbCommand, string>, TestDbHandler>();
        services.AddScoped<IHandler<CheckUpdateCommand, CheckUpdateResult>, CheckUpdateHandler>();
        services.AddScoped<IHandler<GetUpdateStatusQuery, GetUpdateStatusResult>, GetUpdateStatusHandler>();

        return services;
    }
}
