namespace Payaffe.Sdk;

public sealed class PayaffeClientOptions
{
    public Uri? BaseAddress { get; set; }

    public string? ApiToken { get; set; }

    public int MaximumRetries { get; set; } = 2;

    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromSeconds(15);

    public double RetryJitterRatio { get; set; } = 0.2;

    internal ValidatedPayaffeClientOptions Validate()
    {
        Uri baseAddress = PayaffeClient.ValidateBaseAddress(BaseAddress);
        string apiToken = PayaffeClient.ValidateApiToken(ApiToken);

        if (MaximumRetries is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumRetries),
                "Maximum retries must be between zero and ten.");
        }

        if (InitialRetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(InitialRetryDelay),
                "The initial retry delay must be greater than zero.");
        }

        if (MaximumRetryDelay < InitialRetryDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumRetryDelay),
                "The maximum retry delay must not be shorter than the initial retry delay.");
        }

        if (RetryJitterRatio is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RetryJitterRatio),
                "The retry jitter ratio must be between zero and one.");
        }

        return new ValidatedPayaffeClientOptions(
            baseAddress,
            apiToken,
            MaximumRetries,
            InitialRetryDelay,
            MaximumRetryDelay,
            RetryJitterRatio);
    }
}

internal sealed record ValidatedPayaffeClientOptions(
    Uri BaseAddress,
    string ApiToken,
    int MaximumRetries,
    TimeSpan InitialRetryDelay,
    TimeSpan MaximumRetryDelay,
    double RetryJitterRatio);
