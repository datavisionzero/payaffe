namespace Payaffe.Application.Payments;

public interface IObservationHealthStore
{
    Task<IReadOnlyList<ObservationHealthReadModel>> ListAsync(
        CancellationToken cancellationToken);

    Task<bool?> IsAvailableAsync(
        string supportedCurrency,
        CancellationToken cancellationToken);

    Task RecordSuccessAsync(
        string supportedCurrency,
        string providerName,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken);

    Task RecordFailureAsync(
        string supportedCurrency,
        string providerName,
        string safeErrorCode,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken);
}

public sealed record ObservationHealthReadModel(
    string SupportedCurrency,
    string ProviderName,
    string Status,
    DateTimeOffset? LastSuccessfulAt,
    DateTimeOffset? LastFailedAt,
    string? LastSafeErrorCode);
