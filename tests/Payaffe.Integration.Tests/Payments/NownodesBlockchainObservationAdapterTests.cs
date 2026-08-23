using System.Net;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace Payaffe.Integration.Tests.Payments;

public sealed class NownodesBlockchainObservationAdapterTests
{
    private static readonly BlockchainObservationTarget BtcTarget = new(
        Guid.Parse("ed80179d-f317-46ac-9491-3a956e4b5299"),
        "BTC",
        "btc-test-address",
        "0.00039980");

    [Fact]
    public async Task PollAsync_maps_bitcoin_like_address_transaction_ids_to_observations()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.AddJson(
            "/api/v2/address/btc-test-address",
            """
            {
              "address": "btc-test-address",
              "txids": ["tx-123", "tx-outgoing"]
            }
            """);
        handler.AddJson(
            "/api/v2/tx/tx-123",
            """
            {
              "txid": "tx-123",
              "vin": [
                {
                  "addresses": ["sender-address"]
                }
              ],
              "vout": [
                {
                  "value": "39980",
                  "addresses": ["btc-test-address"]
                },
                {
                  "value": "1000",
                  "addresses": ["change-address"]
                }
              ],
              "confirmations": 3,
              "blockTime": 1783166700
            }
            """);
        handler.AddJson(
            "/api/v2/tx/tx-outgoing",
            """
            {
              "txid": "tx-outgoing",
              "vin": [
                {
                  "addresses": ["btc-test-address"]
                }
              ],
              "vout": [
                {
                  "value": "500",
                  "addresses": ["other-address"]
                }
              ],
              "confirmations": 1,
              "blockTime": 1783166760
            }
            """);
        var adapter = CreateAdapter(handler);

        var observations = await adapter.PollAsync(BtcTarget, CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal("tx-123", observation.TransactionHash);
        Assert.Equal("0.0003998", observation.ObservedAmount);
        Assert.Equal(DateTimeOffset.Parse("2026-07-04T12:05:00Z"), observation.ObservedAt);
        Assert.Equal(3, observation.Confirmations);
        Assert.Equal("nownodes", observation.ProviderName);
        Assert.Equal("nownodes:tx-123", observation.ProviderObservationId);
        Assert.Equal(
            ["/api/v2/address/btc-test-address", "/api/v2/tx/tx-123", "/api/v2/tx/tx-outgoing"],
            handler.RequestPaths);
        Assert.All(handler.ApiKeys, apiKey => Assert.Equal("configured-api-key", apiKey));
    }

    [Fact]
    public async Task PollAsync_uses_litecoin_nownodes_base_url_for_ltc_targets()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.AddJson(
            "/api/v2/address/ltc-test-address",
            """
            {
              "address": "ltc-test-address",
              "txids": []
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
        var request = Assert.Single(handler.Requests);
        Assert.Equal("ltcbook.test", request.Host);
        Assert.Equal("/api/v2/address/ltc-test-address", request.AbsolutePath);
    }

    [Fact]
    public async Task PollAsync_maps_ethereum_native_transfer_to_observation()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.AddJson(
            "/api/v2/address/0xabc",
            """
            {
              "address": "0xabc",
              "txids": ["0xtransaction"]
            }
            """);
        handler.AddJson(
            "/api/v2/tx/0xtransaction",
            """
            {
              "txid": "0xtransaction",
              "vin": [
                {
                  "addresses": ["0xsender"]
                }
              ],
              "vout": [
                {
                  "value": "399800000000000",
                  "addresses": ["0xabc"]
                }
              ],
              "confirmations": 6,
              "blockTime": "1783166700"
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
        Assert.Equal("nownodes", observation.ProviderName);
        Assert.Equal(
            ["eth-blockbook.test"],
            handler.Requests.Select(request => request.Host).Distinct().ToArray());
    }

    [Fact]
    public async Task PollAsync_limits_transaction_detail_requests()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.AddJson(
            "/api/v2/address/btc-test-address",
            """
            {
              "address": "btc-test-address",
              "txids": ["tx-1", "tx-2", "tx-3"]
            }
            """);
        handler.AddJson(
            "/api/v2/tx/tx-1",
            """
            {
              "txid": "tx-1",
              "vin": [],
              "vout": [
                {
                  "value": "1000",
                  "addresses": ["btc-test-address"]
                }
              ],
              "confirmations": 1,
              "blockTime": 1783166700
            }
            """);
        handler.AddJson(
            "/api/v2/tx/tx-2",
            """
            {
              "txid": "tx-2",
              "vin": [],
              "vout": [
                {
                  "value": "2000",
                  "addresses": ["btc-test-address"]
                }
              ],
              "confirmations": 1,
              "blockTime": 1783166760
            }
            """);
        var adapter = CreateAdapter(
            handler,
            new NownodesBlockchainObservationProviderOptions
            {
                BtcBaseUrl = new Uri("https://btcbook.test"),
                LtcBaseUrl = new Uri("https://ltcbook.test"),
                EthBaseUrl = new Uri("https://eth-blockbook.test"),
                ApiKeyReference = "configuration:BlockchainObservation:ProviderSecrets:nownodes",
                MaxTransactionsPerAddressPoll = 2,
            });

        var observations = await adapter.PollAsync(BtcTarget, CancellationToken.None);

        Assert.Equal(2, observations.Count);
        Assert.DoesNotContain("/api/v2/tx/tx-3", handler.RequestPaths);
    }

    [Fact]
    public async Task PollAsync_clamps_configured_transaction_limit_to_at_least_one()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.AddJson(
            "/api/v2/address/btc-test-address",
            """
            {
              "address": "btc-test-address",
              "txids": ["tx-1", "tx-2"]
            }
            """);
        handler.AddJson(
            "/api/v2/tx/tx-1",
            """
            {
              "txid": "tx-1",
              "vin": [],
              "vout": [
                {
                  "value": "1000",
                  "addresses": ["btc-test-address"]
                }
              ],
              "confirmations": 1,
              "blockTime": 1783166700
            }
            """);
        var adapter = CreateAdapter(
            handler,
            new NownodesBlockchainObservationProviderOptions
            {
                BtcBaseUrl = new Uri("https://btcbook.test"),
                LtcBaseUrl = new Uri("https://ltcbook.test"),
                EthBaseUrl = new Uri("https://eth-blockbook.test"),
                ApiKeyReference = "configuration:BlockchainObservation:ProviderSecrets:nownodes",
                MaxTransactionsPerAddressPoll = 0,
            });

        var observations = await adapter.PollAsync(BtcTarget, CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal("tx-1", observation.TransactionHash);
        Assert.DoesNotContain("/api/v2/tx/tx-2", handler.RequestPaths);
    }

    [Fact]
    public async Task PollAsync_skips_missing_transaction_details()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.AddJson(
            "/api/v2/address/btc-test-address",
            """
            {
              "address": "btc-test-address",
              "txids": ["tx-missing", "tx-present"]
            }
            """);
        handler.AddJson(
            "/api/v2/tx/tx-present",
            """
            {
              "txid": "tx-present",
              "vin": [],
              "vout": [
                {
                  "value": "39980",
                  "addresses": ["btc-test-address"]
                }
              ],
              "confirmations": 2,
              "blockTime": 1783166700
            }
            """);
        var adapter = CreateAdapter(handler);

        var observations = await adapter.PollAsync(BtcTarget, CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal("tx-present", observation.TransactionHash);
        Assert.Equal(
            ["/api/v2/address/btc-test-address", "/api/v2/tx/tx-missing", "/api/v2/tx/tx-present"],
            handler.RequestPaths);
    }

    [Fact]
    public async Task PollAsync_maps_script_pub_key_and_addr_address_shapes()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.AddJson(
            "/api/v2/address/btc-test-address",
            """
            {
              "address": "btc-test-address",
              "txids": ["tx-script", "tx-addr"]
            }
            """);
        handler.AddJson(
            "/api/v2/tx/tx-script",
            """
            {
              "txid": "tx-script",
              "vin": [],
              "vout": [
                {
                  "value": "1000",
                  "scriptPubKey": {
                    "addresses": ["btc-test-address"]
                  }
                }
              ],
              "confirmations": 1,
              "blockTime": 1783166700
            }
            """);
        handler.AddJson(
            "/api/v2/tx/tx-addr",
            """
            {
              "txid": "tx-addr",
              "vin": [],
              "vout": [
                {
                  "value": "2000",
                  "addr": "btc-test-address"
                }
              ],
              "confirmations": 1,
              "blockTime": 1783166760
            }
            """);
        var adapter = CreateAdapter(handler);

        var observations = await adapter.PollAsync(BtcTarget, CancellationToken.None);

        Assert.Equal(["tx-script", "tx-addr"], observations.Select(observation => observation.TransactionHash).ToArray());
        Assert.Equal(["0.00001", "0.00002"], observations.Select(observation => observation.ObservedAmount).ToArray());
    }

    [Fact]
    public async Task PollAsync_returns_empty_observations_when_address_response_has_no_txids()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.AddJson(
            "/api/v2/address/btc-test-address",
            """
            {
              "address": "btc-test-address"
            }
            """);
        var adapter = CreateAdapter(handler);

        var observations = await adapter.PollAsync(BtcTarget, CancellationToken.None);

        Assert.Empty(observations);
        Assert.Equal(["/api/v2/address/btc-test-address"], handler.RequestPaths);
    }

    [Fact]
    public async Task PollAsync_returns_empty_observations_for_missing_address()
    {
        var handler = new RoutingHttpMessageHandler();
        var adapter = CreateAdapter(handler);

        var observations = await adapter.PollAsync(BtcTarget, CancellationToken.None);

        Assert.Empty(observations);
        Assert.Equal(["/api/v2/address/btc-test-address"], handler.RequestPaths);
    }

    [Fact]
    public async Task PollAsync_rejects_unsupported_currency()
    {
        var handler = new RoutingHttpMessageHandler();
        var adapter = CreateAdapter(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.PollAsync(
                BtcTarget with
                {
                    SupportedCurrency = "DOGE",
                },
                CancellationToken.None));

        Assert.Contains("Unsupported NOWNodes Blockchain Observation currency", exception.Message, StringComparison.Ordinal);
        Assert.Empty(handler.RequestPaths);
    }

    private static NownodesBlockchainObservationAdapter CreateAdapter(
        RoutingHttpMessageHandler handler,
        NownodesBlockchainObservationProviderOptions? nownodesOptions = null)
    {
        var httpClient = new HttpClient(handler);
        return new NownodesBlockchainObservationAdapter(
            httpClient,
            Options.Create(new BlockchainObservationOptions
            {
                Nownodes = nownodesOptions ?? new NownodesBlockchainObservationProviderOptions
                {
                    BtcBaseUrl = new Uri("https://btcbook.test"),
                    LtcBaseUrl = new Uri("https://ltcbook.test"),
                    EthBaseUrl = new Uri("https://eth-blockbook.test"),
                    ApiKeyReference = "configuration:BlockchainObservation:ProviderSecrets:nownodes",
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

    private sealed class RoutingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses = [];
        private readonly List<Uri> _requests = [];
        private readonly List<string> _apiKeys = [];

        public IReadOnlyList<Uri> Requests => _requests;

        public IReadOnlyList<string> RequestPaths => _requests
            .Select(request => request.AbsolutePath)
            .ToArray();

        public IReadOnlyList<string> ApiKeys => _apiKeys;

        public void AddJson(string path, string responseBody)
        {
            _responses[path] = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests.Add(request.RequestUri!);
            _apiKeys.Add(request.Headers.TryGetValues("api-key", out var values)
                ? Assert.Single(values)
                : string.Empty);

            if (!_responses.TryGetValue(request.RequestUri!.AbsolutePath, out var responseBody))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody),
            });
        }
    }
}
