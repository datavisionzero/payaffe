using Payaffe.Application.Admin;
using Payaffe.Application.Payments;
using Payaffe.Application.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Payaffe.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPayaffeApplication(this IServiceCollection services)
    {
        services.AddOptions<PaymentApplicationOptions>();
        services.AddOptions<AdminAuthenticationOptions>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPayerPageIdGenerator, PayerPageIdGenerator>();
        services.AddSingleton<IAdminTotpVerifier, AdminTotpVerifier>();
        services.TryAddScoped<IExchangeRateSource, UnavailableExchangeRateSource>();
        services.TryAddScoped<IExchangeRateSecretResolver, UnavailableExchangeRateSecretResolver>();
        services.TryAddScoped<IPaymentAddressProvider, UnavailablePaymentAddressProvider>();
        services.TryAddScoped<IBlockchainObservationAdapter, NoOpBlockchainObservationAdapter>();
        services.TryAddScoped<IWebhookSecretResolver, UnavailableWebhookSecretResolver>();
        services.TryAddScoped<IAdminTotpSecretResolver, UnavailableAdminTotpSecretResolver>();
        services.AddScoped<PaymentApplicationService>();
        services.AddScoped<AdminAuthenticationService>();
        services.AddScoped<AdminPaymentQueryService>();
        services.AddScoped<AdminAuditLogQueryService>();
        services.AddScoped<AdminWebhookDeliveryQueryService>();
        services.AddScoped<AdminIntegrationApiCredentialService>();
        services.AddScoped<AdminWebhookEndpointService>();
        services.AddScoped<AdminNativeEthAddressPoolService>();

        return services;
    }
}
