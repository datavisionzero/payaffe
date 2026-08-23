using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace Payaffe.Integration.Tests.Payments;

public sealed class ConfiguredBlockchainObservationAdapterTests
{
    private static readonly BlockchainObservationTarget Target = new(
        Guid.Parse("7114606d-93ef-4f1e-bcc6-ad8479387d9a"),
        "BTC",
        "btc-test-address",
        "0.00039980");

    [Fact]
    public async Task StartWatchingAsync_dispatches_to_selected_mode_adapter()
    {
        var blockchairAdapter = new CapturingObservationAdapter();
        var adapter = new ConfiguredBlockchainObservationAdapter(
            Options.Create(new BlockchainObservationOptions
            {
                Mode = "blockchair",
            }),
            new FixedAdapterResolver(blockchairAdapter));

        await adapter.StartWatchingAsync(Target, CancellationToken.None);

        Assert.Equal(1, blockchairAdapter.StartWatchingCount);
        Assert.Equal(Target, blockchairAdapter.Target);
    }

    [Fact]
    public async Task PollAsync_dispatches_to_selected_mode_adapter()
    {
        var observation = new BlockchainObservation(
            "tx-123",
            "0.00039980",
            DateTimeOffset.Parse("2026-07-04T12:05:00Z"),
            Confirmations: 1,
            "test-provider",
            "provider-observation-123");
        var nownodesAdapter = new CapturingObservationAdapter(observation);
        var adapter = new ConfiguredBlockchainObservationAdapter(
            Options.Create(new BlockchainObservationOptions
            {
                Mode = " NOWNODES ",
            }),
            new FixedAdapterResolver(nownodesAdapter));

        var observations = await adapter.PollAsync(Target, CancellationToken.None);

        Assert.Equal(1, nownodesAdapter.PollCount);
        Assert.Equal(Target, nownodesAdapter.Target);
        Assert.Same(observation, Assert.Single(observations));
    }

    [Fact]
    public async Task Blank_mode_dispatches_to_none_adapter()
    {
        var noneAdapter = new CapturingObservationAdapter();
        var adapter = new ConfiguredBlockchainObservationAdapter(
            Options.Create(new BlockchainObservationOptions
            {
                Mode = "   ",
            }),
            new FixedAdapterResolver(noneAdapter));

        await adapter.StartWatchingAsync(Target, CancellationToken.None);

        Assert.Equal("none", noneAdapter.Mode);
        Assert.Equal(1, noneAdapter.StartWatchingCount);
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("blockchair", true)]
    [InlineData("nownodes", true)]
    public async Task Reports_configured_observation_availability(
        string mode,
        bool expectedAvailable)
    {
        var adapter = new ConfiguredBlockchainObservationAdapter(
            Options.Create(new BlockchainObservationOptions { Mode = mode }),
            new FixedAdapterResolver(new CapturingObservationAdapter()));

        var available = await adapter.IsObservationAvailableAsync(
            "BTC",
            CancellationToken.None);

        Assert.Equal(expectedAvailable, available);
    }

    private sealed class FixedAdapterResolver(IBlockchainObservationAdapter adapter)
        : IBlockchainObservationAdapterResolver
    {
        public IBlockchainObservationAdapter Resolve(string mode)
        {
            if (adapter is CapturingObservationAdapter capturingAdapter)
            {
                capturingAdapter.Mode = mode;
            }

            return adapter;
        }
    }

    private sealed class CapturingObservationAdapter(params BlockchainObservation[] observations)
        : IBlockchainObservationAdapter
    {
        public string? Mode { get; set; }

        public BlockchainObservationTarget? Target { get; private set; }

        public int StartWatchingCount { get; private set; }

        public int PollCount { get; private set; }

        public Task StartWatchingAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken)
        {
            Target = target;
            StartWatchingCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BlockchainObservation>> PollAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken)
        {
            Target = target;
            PollCount++;
            return Task.FromResult<IReadOnlyList<BlockchainObservation>>(observations);
        }
    }
}
