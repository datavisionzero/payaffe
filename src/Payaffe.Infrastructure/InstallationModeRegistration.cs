using Payaffe.Application.Installation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Infrastructure;

public static class InstallationModeRegistration
{
    /// <summary>
    /// Registers the configured Installation Mode. Called before
    /// <see cref="DependencyInjection.AddPayaffeInfrastructure"/>, because what
    /// that registers depends on it: the simulations of Test Mode are not
    /// registered at all in a live installation.
    /// </summary>
    public static ConfiguredInstallationMode AddPayaffeInstallationMode(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var mode = ConfiguredInstallationMode.FromConfiguration(
            configuration[ConfiguredInstallationMode.ConfigurationKey]);
        services.AddSingleton(mode);
        return mode;
    }

    /// <summary>
    /// The mode registered so far, registering <c>live</c> when nothing was.
    /// </summary>
    internal static ConfiguredInstallationMode ResolveInstallationMode(this IServiceCollection services)
    {
        var registered = services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(ConfiguredInstallationMode))?
            .ImplementationInstance as ConfiguredInstallationMode;
        if (registered is not null)
        {
            return registered;
        }

        services.AddSingleton(ConfiguredInstallationMode.Live);
        return ConfiguredInstallationMode.Live;
    }
}
