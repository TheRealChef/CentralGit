using CentralGit.Application.Ports;
using CentralGit.Infrastructure.FileSystem;
using CentralGit.Infrastructure.Git;
using CentralGit.Infrastructure.Storage;
using CentralGit.Infrastructure.Updates;
using CentralGit.Infrastructure.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace CentralGit.Infrastructure;

public static class DependencyInjection
{
    /// <summary>The adapters behind every port: git CLI, JSON files in %APPDATA%\CentralGit, Credential Manager, the file system, Velopack updates.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<GitRunner>(_ => new GitRunner());
        services.AddSingleton<IGitRepositoryFactory, GitRepositoryFactory>();
        services.AddSingleton<IRepositoryGlances, RepositoryStatusCache>();
        services.AddSingleton<IGitAccounts, GitAccounts>();
        services.AddSingleton<IRepositoryFileSystem, RepositoryFileSystem>();
        services.AddSingleton<ISettingsStore>(_ => new SettingsStore());
        services.AddSingleton<ILayoutStore>(_ => new LayoutStore());
        services.AddSingleton<ICredentialStore, CredentialVault>();
        services.AddSingleton<IAppUpdater, VelopackUpdater>();
        return services;
    }
}
