using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

public sealed class ExchangeRateOptionsValidator : IValidateOptions<ExchangeRateOptions>
{
    public ValidateOptionsResult Validate(string? name, ExchangeRateOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (options.BaseUrl is null ||
            !options.BaseUrl.IsAbsoluteUri ||
            options.BaseUrl.Scheme is not ("https" or "http"))
        {
            failures.Add("ExchangeRates:BaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        if (options.CacheInterval <= TimeSpan.Zero)
        {
            failures.Add("ExchangeRates:CacheInterval must be positive.");
        }

        if (options.MaxStaleAge < options.CacheInterval)
        {
            failures.Add("ExchangeRates:MaxStaleAge must be at least CacheInterval.");
        }

        if (options.RequestTimeout <= TimeSpan.Zero ||
            options.RequestTimeout > TimeSpan.FromMinutes(1))
        {
            failures.Add("ExchangeRates:RequestTimeout must be positive and no more than one minute.");
        }

        if (!string.IsNullOrWhiteSpace(options.ApiKeyReference) &&
            ConfigurationExchangeRateSecretResolver.GetConfigurationKey(options.ApiKeyReference) is null)
        {
            failures.Add(
                "ExchangeRates:ApiKeyReference must use " +
                "'configuration:ExchangeRates:ProviderSecrets:<name>'.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
