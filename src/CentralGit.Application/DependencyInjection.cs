using CentralGit.Application.Features.Accounts;
using CentralGit.Application.Features.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace CentralGit.Application;

public static class DependencyInjection
{
    /// <summary>The app-wide use cases. Per-repository services come from <see cref="IRepositoryService.Open"/>.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IRepositoryService, RepositoryService>();
        services.AddSingleton<IAccountService, AccountService>();
        return services;
    }
}
