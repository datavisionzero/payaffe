using Payaffe.Application.Admin;
using Payaffe.Application.Payments;
using Payaffe.Application.Webhooks;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Telemetry;
using Payaffe.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure;

public static class DependencyInjection
{
    /// <param name="registerHostedWorkers">
    /// Registers the scheduled background workers. Hosts that only serve
    /// requests, such as the local Admin MCP host, pass <c>false</c> so they
    /// never take a worker lease or process a batch.
    /// </param>
    /// <param name="applySchemaOnStartup">
    /// Brings the schema up to date while the host starts (ADR 0027). The
    /// <c>api</c> and <c>worker</c> hosts pass <c>true</c>. It stays off by
    /// default so that the local Admin MCP host, the <c>migrations</c> host
    /// and the tests decide for themselves when a schema is applied.
    /// </param>
    public static IServiceCollection AddPayaffeInfrastructure(
        this IServiceCollection services,
        string connectionString,
        bool registerHostedWorkers = true,
        bool applySchemaOnStartup = false)
    {
        var installationMode = services.ResolveInstallationMode();
        services.AddDbContext<PayaffeDbContext>(options => options.UseNpgsql(connectionString));
        services.AddOptions<PaymentApplicationOptions>();
        services.AddSingleton<IValidateOptions<PaymentApplicationOptions>, PaymentApplicationOptionsValidator>();

        // A host that does not migrate takes the schema as given, so its
        // workers must not wait for a migration that is never going to run
        // there. That is the whole difference between the two registrations.
        if (applySchemaOnStartup)
        {
            services.AddSingleton<SchemaMigrationState>();
            services.AddHostedService<SchemaMigrationHostedService>();
        }
        else
        {
            services.AddSingleton(SchemaMigrationState.AlreadyApplied());
        }

        services.AddSingleton<IAdminPasswordHasher, AdminPasswordHasher>();
        services.AddSingleton<IAdminSessionTokenService, AdminSessionTokenService>();
        services.AddSingleton<IIntegrationApiCredentialTokenService, IntegrationApiCredentialTokenService>();
        services.AddScoped<IAdminTotpSecretResolver, ConfigurationAdminTotpSecretResolver>();
        services.AddScoped<IAdminSecurityStore, EfAdminSecurityStore>();
        services.AddScoped<IAdminProjectStore, EfAdminProjectStore>();
        services.AddScoped<IAdminPaymentStore, EfAdminPaymentStore>();
        services.AddScoped<IAdminAuditLogStore, EfAdminAuditLogStore>();
        services.AddScoped<IAdminWebhookDeliveryStore, EfAdminWebhookDeliveryStore>();
        services.AddScoped<IAdminIntegrationApiCredentialStore, EfAdminIntegrationApiCredentialStore>();
        services.AddScoped<IAdminWebhookEndpointStore, EfAdminWebhookEndpointStore>();
        services.AddScoped<IAdminNativeEthAddressPoolStore, EfAdminNativeEthAddressPoolStore>();
        services.AddScoped<IPaymentStore, EfPaymentStore>();
        services.AddScoped<IProjectPaymentConfigurationStore, EfProjectPaymentConfigurationStore>();
        if (installationMode.IsTest)
        {
            services.AddScoped<IPaymentAddressProvider, SimulatedPaymentAddressProvider>();
        }
        else
        {
            services.AddScoped<IPaymentAddressProvider, EfPaymentAddressProvider>();
        }

        services.AddScoped<IRateCacheStore, EfRateCacheStore>();
        services.AddScoped<IObservationHealthStore, EfObservationHealthStore>();
        services.AddScoped<BackgroundWorkerLeaseManager>();
        services.AddScoped<IIntegrationApiCredentialAuthenticator, EfIntegrationApiCredentialAuthenticator>();
        services.AddScoped<IExchangeRateSecretResolver, ConfigurationExchangeRateSecretResolver>();
        services.AddSingleton<IValidateOptions<ExchangeRateOptions>, ExchangeRateOptionsValidator>();
        services.AddOptions<ExchangeRateOptions>();
        if (installationMode.IsTest)
        {
            services.AddSingleton<IValidateOptions<SimulatedExchangeRateOptions>, SimulatedExchangeRateOptionsValidator>();
            services.AddOptions<SimulatedExchangeRateOptions>();
            services.AddScoped<SimulatedExchangeRateSource>();
            services.AddScoped<IExchangeRateSource>(
                serviceProvider => serviceProvider.GetRequiredService<SimulatedExchangeRateSource>());
            services.AddScoped<IRateCacheRefresher>(
                serviceProvider => serviceProvider.GetRequiredService<SimulatedExchangeRateSource>());
        }
        else
        {
            services.AddHttpClient<CoinGeckoExchangeRateSource>(IdentifyProduct);
            services.AddScoped<IExchangeRateSource>(
                serviceProvider => serviceProvider.GetRequiredService<CoinGeckoExchangeRateSource>());
            services.AddScoped<IRateCacheRefresher>(
                serviceProvider => serviceProvider.GetRequiredService<CoinGeckoExchangeRateSource>());
        }

        services.AddSingleton<IValidateOptions<RateCacheRefreshWorkerOptions>, RateCacheRefreshWorkerOptionsValidator>();
        services.AddOptions<RateCacheRefreshWorkerOptions>();
        if (registerHostedWorkers)
        {
            services.AddHostedService<RateCacheRefreshHostedService>();
        }

        services.AddSingleton<IValidateOptions<PaymentAddressOptions>, PaymentAddressOptionsValidator>();
        services.AddOptions<PaymentAddressOptions>();
        services.AddScoped<IBlockchainObservationSecretResolver, ConfigurationBlockchainObservationSecretResolver>();
        services.AddScoped<IBlockchainObservationAdapterResolver, ServiceProviderBlockchainObservationAdapterResolver>();
        services.AddScoped<NoOpBlockchainObservationAdapter>();
        services.AddHttpClient<BlockchairBlockchainObservationAdapter>(IdentifyProduct);
        services.AddHttpClient<NownodesBlockchainObservationAdapter>(IdentifyProduct);
        services.AddScoped<ConfiguredBlockchainObservationAdapter>();
        services.AddScoped<IBlockchainObservationAdapter, ObservationHealthTrackingAdapter>();
        services.AddSingleton<IValidateOptions<BlockchainObservationOptions>, BlockchainObservationOptionsValidator>();
        if (installationMode.IsTest)
        {
            // Unset means simulated here, so a Test Mode installation needs no
            // observation setting at all. Anything else is refused by the
            // validator above.
            services.PostConfigure<BlockchainObservationOptions>(options =>
            {
                if (BlockchainObservationOptions.NormalizeMode(options.Mode) == "none")
                {
                    options.Mode = BlockchainObservationOptions.SimulatedMode;
                }
            });
            services.AddScoped<SimulatedBlockchainObservationAdapter>();
            services.AddScoped<ISimulatedTransactionStore, EfSimulatedTransactionStore>();
            services.AddScoped<PaymentSimulationService>();
        }

        services.AddScoped<IWebhookSecretResolver, ConfigurationWebhookSecretResolver>();
        services.AddOptions<PaymentLifecycleWorkerOptions>();
        services.AddSingleton<IValidateOptions<PaymentLifecycleWorkerOptions>, PaymentLifecycleWorkerOptionsValidator>();
        if (registerHostedWorkers)
        {
            services.AddHostedService<PaymentLifecycleHostedService>();
        }

        services.AddOptions<BlockchainObservationOptions>();
        services.AddOptions<BlockchainObservationWorkerOptions>();
        services.AddSingleton<IValidateOptions<BlockchainObservationWorkerOptions>, BlockchainObservationWorkerOptionsValidator>();
        if (registerHostedWorkers)
        {
            services.AddHostedService<BlockchainObservationHostedService>();
        }

        services.AddOptions<ReorgMonitoringWorkerOptions>();
        services.AddSingleton<IValidateOptions<ReorgMonitoringWorkerOptions>, ReorgMonitoringWorkerOptionsValidator>();
        if (registerHostedWorkers)
        {
            services.AddHostedService<ReorgMonitoringHostedService>();
        }

        services.AddOptions<WebhookDeliveryOptions>();
        services.AddSingleton<IValidateOptions<WebhookDeliveryOptions>, WebhookDeliveryOptionsValidator>();
        services.AddSingleton(serviceProvider => WebhookTargetPolicy.Parse(
            serviceProvider.GetRequiredService<IOptions<WebhookDeliveryOptions>>().Value.AllowedPrivateTargets));
        services.AddHttpClient<WebhookDeliveryProcessor>((serviceProvider, client) =>
            {
                IdentifyProduct(client);
                client.Timeout = serviceProvider.GetRequiredService<IOptions<WebhookDeliveryOptions>>().Value.RequestTimeout;
            })
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
                WebhookDeliveryHttpHandler.Create(serviceProvider.GetRequiredService<WebhookTargetPolicy>()));
        if (registerHostedWorkers)
        {
            services.AddHostedService<WebhookDeliveryHostedService>();
        }

        services.AddOptions<OperationalMetricsOptions>();
        if (registerHostedWorkers)
        {
            services.AddHostedService<OperationalMetricsHostedService>();
        }

        return services;
    }

    /// <summary>
    /// Every outbound client identifies the product. See
    /// <see cref="ProductUserAgent"/> for why this is required rather than
    /// polite.
    /// </summary>
    private static void IdentifyProduct(HttpClient client) =>
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ProductUserAgent.Value);
}
