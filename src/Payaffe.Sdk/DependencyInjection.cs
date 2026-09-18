using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Payaffe.Sdk;

public interface IPayaffeClientFactory
{
    PayaffeClient CreateClient(string name);
}

public static class PayaffeServiceCollectionExtensions
{
    public static IHttpClientBuilder AddPayaffeClient(
        this IServiceCollection services,
        Action<PayaffeClientOptions> configure)
    {
        IHttpClientBuilder builder = AddPayaffeClientCore(services, Options.DefaultName, configure);
        services.TryAddTransient(serviceProvider =>
            serviceProvider.GetRequiredService<IPayaffeClientFactory>()
                .CreateClient(Options.DefaultName));
        return builder;
    }

    public static IHttpClientBuilder AddPayaffeClient(
        this IServiceCollection services,
        string name,
        Action<PayaffeClientOptions> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return AddPayaffeClientCore(services, name, configure);
    }

    private static IHttpClientBuilder AddPayaffeClientCore(
        IServiceCollection services,
        string name,
        Action<PayaffeClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(name, configure);
        services.TryAddSingleton<IPayaffeClientFactory, PayaffeClientFactory>();
        return services.AddHttpClient(PayaffeClientFactory.GetHttpClientName(name));
    }
}

internal sealed class PayaffeClientFactory(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<PayaffeClientOptions> options) : IPayaffeClientFactory
{
    private const string _httpClientNamePrefix = "Payaffe.Sdk:";

    public PayaffeClient CreateClient(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ValidatedPayaffeClientOptions validatedOptions = options.Get(name).Validate();
        HttpClient httpClient = httpClientFactory.CreateClient(GetHttpClientName(name));
        return new PayaffeClient(httpClient, validatedOptions);
    }

    internal static string GetHttpClientName(string name) => $"{_httpClientNamePrefix}{name}";
}
