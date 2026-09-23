using System.Net;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace Payaffe.Integration.Tests.Payments;

public sealed class BlockchairBlockchainObservationAdapterTests
{
    private static readonly BlockchainObservationTarget BtcTarget = new(
        Guid.Parse("ed80179d-f317-46ac-9491-3a956e4b5299"),
        "BTC",
        "btc-test-address",
        "0.00039980");

    [Fact]
    public async Task PollAsync_maps_bitcoin_like_address_transactions_to_observations()
    {
        var handler = new CapturingHttpMessageHandler("""
            {
              "data": {
                "btc-test-address": {
                  "transactions": [
                    {
                      "block_id": 840000,
                      "hash": "tx-123",
                      "time": "2026-07-04 12:05:00",
                      "balance_change": 39980
                    },
                    {
                      "block_id": 840001,
                      "hash": "tx-spend",
                      "time": "2026-07-04 12:06:00",
                      "balance_change": -500
                    }
                  ],
                  "utxo": []
                }
              },
              "context": {
                "code": 200,
                "state": 840002
              }
            }
            """);
        var adapter = CreateAdapter(handler);

        var observations = await adapter.PollAsync(BtcTarget, CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal("tx-123", observation.TransactionHash);
        Assert.Equal("0.0003998", observation.ObservedAmount);
        Assert.Equal(DateTimeOffset.Parse("2026-07-04T12:05:00Z"), observation.ObservedAt);
        Assert.Equal(3, observation.Confirmations);
        Assert.Equal("blockchair", observation.ProviderName);
        Assert.Null(observation.BlockHash);
        Assert.Equal(840000, observation.BlockHeight);
        Assert.Equal("blockchair:tx-123", observation.ProviderObservationId);
        Assert.NotNull(handler.RequestUri);
        Assert.Equal(
            "/bitcoin/dashboards/address/btc-test-address",
            handler.RequestUri.AbsolutePath);
        Assert.Contains("transaction_details=true", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("limit=10,0", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("key=configured-api-key", handler.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PollAsync_uses_litecoin_blockchair_chain_for_ltc_targets()
    {
        var handler = new CapturingHttpMessageHandler("""
            {
              "data": {},
              "context": {
                "code": 200,
                "state": 3000000
              }
            }
            """);
        var adapter = CreateAdapter(handler);

        var observations = await adapter.PollAsync(
            BtcTarget with
            {
                SupportedCurrency = "LTC",
                PaymentAddress = "ltc-test-address",
            },
            CancellationToken.None);

        Assert.Empty(observations);
        Assert.NotNull(handler.RequestUri);
        Assert.Equal(
            "/litecoin/dashboards/address/ltc-test-address",
            handler.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task PollAsync_maps_ethereum_incoming_calls_to_observations()
    {
        var handler = new CapturingHttpMessageHandler("""
            {
              "data": {
                "0xabc": {
                  "address": {},
                  "calls": [
                    {
                      "block_id": 200,
                      "transaction_hash": "0xtransaction",
                      "time": "2026-07-04 12:05:00",
                      "sender": "0xsender",
                      "recipient": "0xabc",
                      "value": "399800000000000",
                      "transferred": true
                    },
                    {
                      "block_id": 201,
                      "transaction_hash": "0xoutgoing",
                      "time": "2026-07-04 12:06:00",
                      "sender": "0xabc",
                      "recipient": "0xrecipient",
                      "value": "1000",
                      "transferred": true
                    },
                    {
                      "block_id": 202,
                      "transaction_hash": "0xnot-transferred",
                      "time": "2026-07-04 12:07:00",
                      "sender": "0xsender",
                      "recipient": "0xabc",
                      "value": "1000",
                      "transferred": false
                    }
                  ]
                }
              },
              "context": {
                "code": 200,
                "state": 205
              }
            }
            """);
        var adapter = CreateAdapter(handler);

        var observations = await adapter.PollAsync(
            BtcTarget with
            {
                SupportedCurrency = "ETH",
                PaymentAddress = "0xabc",
            },
            CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal("0xtransaction", observation.TransactionHash);
        Assert.Equal("0.0003998", observation.ObservedAmount);
        Assert.Equal(6, observation.Confirmations);
        Assert.Equal("blockchair", observation.ProviderName);
        Assert.NotNull(handler.RequestUri);
        Assert.Equal(
            "/ethereum/dashboards/address/0xabc",
            handler.RequestUri.AbsolutePath);
        Assert.Contains("state=latest", handler.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PollAsync_returns_empty_observations_for_missing_address_data()
    {
        var handler = new CapturingHttpMessageHandler("""
            {
              "data": {},
              "context": {
                "code": 200,
                "state": 840002
              }
            }
            """);
        var adapter = CreateAdapter(handler);

        var observations = await adapter.PollAsync(BtcTarget, CancellationToken.None);

        Assert.Empty(observations);
    }

    [Fact]
    public async Task PollAsync_returns_empty_observations_for_provider_not_found()
    {
        var handler = new CapturingHttpMessageHandler("", HttpStatusCode.NotFound);
        var adapter = CreateAdapter(handler);

        var observations = await adapter.PollAsync(BtcTarget, CancellationToken.None);

        Assert.Empty(observations);
        Assert.NotNull(handler.RequestUri);
    }

    [Fact]
    public async Task PollAsync_clamps_configured_transaction_limit_to_provider_bounds()
    {
        var handler = new CapturingHttpMessageHandler("""
            {
              "data": {},
              "context": {
                "code": 200,
                "state": 840002
              }
            }
            """);
        var adapter = CreateAdapter(
            handler,
            new BlockchainObservationProviderOptions
            {
                BaseUrl = new Uri("https://blockchair.test"),
                ApiKeyReference = "configuration:BlockchainObservation:ProviderSecrets:blockchair",
                MaxTransactionsPerAddressPoll = 500,
            });

        var observations = await adapter.PollAsync(BtcTarget, CancellationToken.None);

        Assert.Empty(observations);
        Assert.NotNull(handler.RequestUri);
        Assert.Contains("limit=100,0", handler.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PollAsync_maps_address_data_case_insensitively()
    {
        var handler = new CapturingHttpMessageHandler("""
            {
              "data": {
                "BTC-TEST-ADDRESS": {
                  "transactions": [
                    {
                      "block_id": 840000,
                      "hash": "tx-case",
                      "time": "2026-07-04 12:05:00",
                      "balance_change": "39980"
                    }
                  ],
                  "utxo": []
                }
              },
              "context": {
                "code": 200,
                "state": 840000
              }
            }
            """);
        var adapter = CreateAdapter(handler);

        var observations = await adapter.PollAsync(BtcTarget, CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal("tx-case", observation.TransactionHash);
        Assert.Equal("0.0003998", observation.ObservedAmount);
        Assert.Equal(1, observation.Confirmations);
    }

    [Fact]
    public async Task PollAsync_skips_malformed_provider_transactions()
    {
        var handler = new CapturingHttpMessageHandler("""
            {
              "data": {
                "btc-test-address": {
                  "transactions": [
                    {
                      "block_id": 840000,
                      "hash": "tx-missing-time",
                      "balance_change": 39980
                    },
                    {
                      "block_id": 840000,
                      "hash": "tx-zero",
                      "time": "2026-07-04 12:05:00",
                      "balance_change": 0
                    },
                    {
                      "block_id": 840000,
                      "hash": "tx-valid",
                      "time": "2026-07-04 12:06:00",
                      "balance_change": 39980
                    }
                  ],
                  "utxo": []
                }
              },
              "context": {
                "code": 200,
                "state": 840001
              }
            }
            """);
        var adapter = CreateAdapter(handler);

        var observations = await adapter.PollAsync(BtcTarget, CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal("tx-valid", observation.TransactionHash);
    }

    [Fact]
    public async Task PollAsync_rejects_unsupported_currency()
    {
        var handler = new CapturingHttpMessageHandler("""{ "data": {}, "context": { "code": 200 } }""");
        var adapter = CreateAdapter(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.PollAsync(
                BtcTarget with
                {
                    SupportedCurrency = "DOGE",
                },
                CancellationToken.None));

        Assert.Contains("Unsupported Blockchair Blockchain Observation currency", exception.Message, StringComparison.Ordinal);
        Assert.Null(handler.RequestUri);
    }

    private static BlockchairBlockchainObservationAdapter CreateAdapter(
        CapturingHttpMessageHandler handler,
        BlockchainObservationProviderOptions? blockchairOptions = null)
    {
        var httpClient = new HttpClient(handler);
        return new BlockchairBlockchainObservationAdapter(
            httpClient,
            Options.Create(new BlockchainObservationOptions
            {
                Blockchair = blockchairOptions ?? new BlockchainObservationProviderOptions
                {
                    BaseUrl = new Uri("https://blockchair.test"),
                    ApiKeyReference = "configuration:BlockchainObservation:ProviderSecrets:blockchair",
                    MaxTransactionsPerAddressPoll = 10,
                },
            }),
            new FixedSecretResolver());
    }

    private sealed class FixedSecretResolver : IBlockchainObservationSecretResolver
    {
        public Task<string?> ResolveAsync(
            string secretReference,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>("configured-api-key");
        }
    }

    private sealed class CapturingHttpMessageHandler(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody),
            });
        }
    }
}
