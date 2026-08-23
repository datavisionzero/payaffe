using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace Payaffe.Infrastructure.Persistence;

public sealed class EfObservationHealthStore(PayaffeDbContext dbContext)
    : IObservationHealthStore
{
    public async Task<IReadOnlyList<ObservationHealthReadModel>> ListAsync(
        CancellationToken cancellationToken) =>
        await dbContext.ObservationHealth
            .AsNoTracking()
            .OrderBy(health => health.SupportedCurrency)
            .Select(health => new ObservationHealthReadModel(
                health.SupportedCurrency,
                health.ProviderName,
                health.Status,
                health.LastSuccessfulAt,
                health.LastFailedAt,
                health.LastSafeErrorCode))
            .ToListAsync(cancellationToken);

    public async Task<bool?> IsAvailableAsync(
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        var status = await dbContext.ObservationHealth
            .AsNoTracking()
            .Where(health => health.SupportedCurrency == supportedCurrency)
            .Select(health => health.Status)
            .SingleOrDefaultAsync(cancellationToken);
        return status is null ? null : status == "available";
    }

    public Task RecordSuccessAsync(
        string supportedCurrency,
        string providerName,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken) =>
        UpsertAsync(supportedCurrency, providerName, "available", null, checkedAt, cancellationToken);

    public Task RecordFailureAsync(
        string supportedCurrency,
        string providerName,
        string safeErrorCode,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken) =>
        UpsertAsync(supportedCurrency, providerName, "unavailable", safeErrorCode, checkedAt, cancellationToken);

    private async Task UpsertAsync(
        string supportedCurrency,
        string providerName,
        string status,
        string? safeErrorCode,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql())
        {
            DateTimeOffset? successfulAt = status == "available" ? checkedAt : null;
            DateTimeOffset? failedAt = status == "unavailable" ? checkedAt : null;
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into app.observation_health (
                    supported_currency, provider_name, status, last_successful_at,
                    last_failed_at, last_safe_error_code, updated_at, version)
                values (
                    {supportedCurrency}, {providerName}, {status},
                    {successfulAt},
                    {failedAt},
                    {safeErrorCode}, {checkedAt}, 1)
                on conflict (supported_currency) do update set
                    provider_name = excluded.provider_name,
                    status = excluded.status,
                    last_successful_at = case
                        when excluded.status = 'available' then excluded.updated_at
                        else app.observation_health.last_successful_at end,
                    last_failed_at = case
                        when excluded.status = 'unavailable' then excluded.updated_at
                        else app.observation_health.last_failed_at end,
                    last_safe_error_code = excluded.last_safe_error_code,
                    updated_at = excluded.updated_at,
                    version = app.observation_health.version + 1
                """,
                cancellationToken);
            return;
        }

        var record = await dbContext.ObservationHealth.FindAsync(
            [supportedCurrency],
            cancellationToken);
        if (record is null)
        {
            record = new ObservationHealthRecord
            {
                SupportedCurrency = supportedCurrency,
                Version = 1,
            };
            dbContext.ObservationHealth.Add(record);
        }
        else
        {
            record.Version++;
        }

        record.ProviderName = providerName;
        record.Status = status;
        record.LastSuccessfulAt = status == "available" ? checkedAt : record.LastSuccessfulAt;
        record.LastFailedAt = status == "unavailable" ? checkedAt : record.LastFailedAt;
        record.LastSafeErrorCode = safeErrorCode;
        record.UpdatedAt = checkedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
