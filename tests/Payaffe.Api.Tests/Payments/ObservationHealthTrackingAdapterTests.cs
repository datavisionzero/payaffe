using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Payaffe.Api.Tests.Payments;

public sealed class ObservationHealthTrackingAdapterTests
{
    [Fact]
    public async Task Provider_failure_and_recovery_update_safe_persisted_health()
    {
        var dbOptions = new DbContextOptionsBuilder<PayaffeDbContext>()
            .UseInMemoryDatabase($"observation-health-{Guid.NewGuid()}")
            .Options;
        await using var dbContext = new PayaffeDbContext(dbOptions);
        var healthStore = new EfObservationHealthStore(dbContext);
        var provider = new SwitchableAdapter { Failure = new HttpRequestException("raw provider detail") };
        var options = Options.Create(new BlockchainObservationOptions { Mode = "blockchair" });
        var configured = new ConfiguredBlockchainObservationAdapter(
            options,
            new FixedResolver(provider));
        var tracking = new ObservationHealthTrackingAdapter(
            configured,
            healthStore,
            new FixedClock(),
            options);
        var target = new BlockchainObservationTarget(
            Guid.NewGuid(),
            "BTC",
            "btc-address",
            "0.01");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            tracking.PollAsync(target, CancellationToken.None));
        Assert.False(await tracking.IsObservationAvailableAsync("BTC", CancellationToken.None));
        var failed = Assert.Single(await healthStore.ListAsync(CancellationToken.None));
        Assert.Equal("observation_provider.http_failure", failed.LastSafeErrorCode);
        Assert.DoesNotContain("raw provider detail", failed.LastSafeErrorCode, StringComparison.Ordinal);

        provider.Failure = null;
        await tracking.PollAsync(target, CancellationToken.None);

        Assert.True(await tracking.IsObservationAvailableAsync("BTC", CancellationToken.None));
        var recovered = Assert.Single(await healthStore.ListAsync(CancellationToken.None));
        Assert.Equal("available", recovered.Status);
        Assert.NotNull(recovered.LastSuccessfulAt);
        Assert.NotNull(recovered.LastFailedAt);
    }

    private sealed class FixedResolver(IBlockchainObservationAdapter adapter)
        : IBlockchainObservationAdapterResolver
    {
        public IBlockchainObservationAdapter Resolve(string mode) => adapter;
    }

    private sealed class SwitchableAdapter : IBlockchainObservationAdapter
    {
        public Exception? Failure { get; set; }

        public Task StartWatchingAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<BlockchainObservation>> PollAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken)
        {
            return Failure is null
                ? Task.FromResult<IReadOnlyList<BlockchainObservation>>([])
                : Task.FromException<IReadOnlyList<BlockchainObservation>>(Failure);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-07-25T12:00:00Z");
    }
}
