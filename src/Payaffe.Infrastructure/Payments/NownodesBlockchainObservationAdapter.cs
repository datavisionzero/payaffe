using System.Globalization;
using System.Net;
using System.Text.Json;
using Payaffe.Application.Payments;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

public sealed class NownodesBlockchainObservationAdapter(
    HttpClient httpClient,
    IOptions<BlockchainObservationOptions> options,
    IBlockchainObservationSecretResolver secretResolver) : IBlockchainObservationAdapter
{
    private const decimal SatoshisPerCoin = 100_000_000m;
    private const decimal WeiPerEther = 1_000_000_000_000_000_000m;
    private const string ProviderName = "nownodes";
    private static readonly Uri DefaultBtcBaseUrl = new("https://btcbook.nownodes.io");
    private static readonly Uri DefaultLtcBaseUrl = new("https://ltcbook.nownodes.io");
    private static readonly Uri DefaultEthBaseUrl = new("https://eth-blockbook.nownodes.io");

    public async Task StartWatchingAsync(
        BlockchainObservationTarget target,
        CancellationToken cancellationToken)
    {
        _ = await ResolveApiKeyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BlockchainObservation>> PollAsync(
        BlockchainObservationTarget target,
        CancellationToken cancellationToken)
    {
        var apiKey = await ResolveApiKeyAsync(cancellationToken);
        var chain = ResolveChain(target.SupportedCurrency);
        var address = target.PaymentAddress;
        var txids = await ReadAddressTransactionIdsAsync(chain.BaseUrl, address, apiKey, cancellationToken);
        if (txids.Count == 0)
        {
            return [];
        }

        var observations = new List<BlockchainObservation>();
        foreach (var txid in txids.Take(GetMaxTransactionsPerAddressPoll()))
        {
            var transaction = await ReadTransactionAsync(chain.BaseUrl, txid, apiKey, cancellationToken);
            if (transaction is null)
            {
                continue;
            }

            if (TryMapObservation(transaction.Value, txid, address, chain.AmountUnit, out var observation))
            {
                observations.Add(observation);
            }
        }

        return observations;
    }

    public async Task<bool> IsObservationAvailableAsync(
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        var apiKeyReference = options.Value.Nownodes.ApiKeyReference;
        return !string.IsNullOrWhiteSpace(apiKeyReference) &&
               !string.IsNullOrWhiteSpace(await secretResolver.ResolveAsync(apiKeyReference, cancellationToken));
    }

    private async Task<string> ResolveApiKeyAsync(CancellationToken cancellationToken)
    {
        var apiKeyReference = options.Value.Nownodes.ApiKeyReference;
        if (string.IsNullOrWhiteSpace(apiKeyReference))
        {
            throw new InvalidOperationException("NOWNodes API key reference is not configured.");
        }

        var apiKey = await secretResolver.ResolveAsync(apiKeyReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("NOWNodes API key reference could not be resolved.");
        }

        return apiKey;
    }

    private async Task<IReadOnlyList<string>> ReadAddressTransactionIdsAsync(
        Uri baseUrl,
        string address,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildRequestUri(baseUrl, $"api/v2/address/{Uri.EscapeDataString(address)}");
        using var request = CreateGetRequest(requestUri, apiKey);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("txids", out var txids) ||
            txids.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var txid in txids.EnumerateArray())
        {
            if (txid.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(txid.GetString()))
            {
                values.Add(txid.GetString()!);
            }
        }

        return values;
    }

    private async Task<JsonElement?> ReadTransactionAsync(
        Uri baseUrl,
        string txid,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildRequestUri(baseUrl, $"api/v2/tx/{Uri.EscapeDataString(txid)}");
        using var request = CreateGetRequest(requestUri, apiKey);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    private static HttpRequestMessage CreateGetRequest(Uri requestUri, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add("api-key", apiKey);
        return request;
    }

    private static Uri BuildRequestUri(Uri baseUrl, string relativePath)
    {
        var baseValue = baseUrl.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUrl
            : new Uri(baseUrl.AbsoluteUri + "/");

        return new Uri(baseValue, relativePath);
    }

    private ChainDefinition ResolveChain(string supportedCurrency)
    {
        return supportedCurrency.ToUpperInvariant() switch
        {
            "BTC" => new ChainDefinition(options.Value.Nownodes.BtcBaseUrl ?? DefaultBtcBaseUrl, SatoshisPerCoin),
            "LTC" => new ChainDefinition(options.Value.Nownodes.LtcBaseUrl ?? DefaultLtcBaseUrl, SatoshisPerCoin),
            "ETH" => new ChainDefinition(options.Value.Nownodes.EthBaseUrl ?? DefaultEthBaseUrl, WeiPerEther),
            _ => throw new InvalidOperationException(
                $"Unsupported NOWNodes Blockchain Observation currency '{supportedCurrency}'."),
        };
    }

    private int GetMaxTransactionsPerAddressPoll()
    {
        return Math.Clamp(options.Value.Nownodes.MaxTransactionsPerAddressPoll, 1, 100);
    }

    private static bool TryMapObservation(
        JsonElement transaction,
        string fallbackTxid,
        string paymentAddress,
        decimal amountUnit,
        out BlockchainObservation observation)
    {
        observation = default!;

        if (IsOutgoingFromPaymentAddress(transaction, paymentAddress) ||
            !TryReadTransactionHash(transaction, fallbackTxid, out var transactionHash) ||
            !TryReadObservedAt(transaction, out var observedAt))
        {
            return false;
        }

        var amount = ReadIncomingAmount(transaction, paymentAddress);
        if (amount <= 0)
        {
            return false;
        }

        var confirmations = TryReadInt32(transaction, "confirmations", out var readConfirmations)
            ? Math.Max(0, readConfirmations)
            : 0;
        // Blockbook reports a height of zero or below for a transaction that
        // is not in a block yet.
        var inBlock = TryReadInt32(transaction, "blockHeight", out var blockHeight) && blockHeight > 0;
        observation = new BlockchainObservation(
            transactionHash,
            FormatCryptoAmount(amount / amountUnit),
            observedAt,
            confirmations,
            ProviderName,
            $"nownodes:{transactionHash}",
            inBlock && TryReadString(transaction, "blockHash", out var blockHash) ? blockHash : null,
            inBlock ? blockHeight : null);
        return true;
    }

    private static bool TryReadTransactionHash(
        JsonElement transaction,
        string fallbackTxid,
        out string transactionHash)
    {
        if (TryReadString(transaction, "txid", out transactionHash))
        {
            return true;
        }

        transactionHash = fallbackTxid;
        return !string.IsNullOrWhiteSpace(transactionHash);
    }

    private static bool IsOutgoingFromPaymentAddress(JsonElement transaction, string paymentAddress)
    {
        if (!transaction.TryGetProperty("vin", out var vin) ||
            vin.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return vin.EnumerateArray().Any(input => ContainsAddress(input, paymentAddress));
    }

    private static decimal ReadIncomingAmount(JsonElement transaction, string paymentAddress)
    {
        if (!transaction.TryGetProperty("vout", out var vout) ||
            vout.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var total = 0m;
        foreach (var output in vout.EnumerateArray())
        {
            if (ContainsAddress(output, paymentAddress) &&
                TryReadPositiveDecimal(output, "value", out var value))
            {
                total += value;
            }
        }

        return total;
    }

    private static bool ContainsAddress(JsonElement element, string paymentAddress)
    {
        if (TryReadString(element, "addr", out var address) &&
            StringComparer.OrdinalIgnoreCase.Equals(address, paymentAddress))
        {
            return true;
        }

        if (TryReadString(element, "address", out address) &&
            StringComparer.OrdinalIgnoreCase.Equals(address, paymentAddress))
        {
            return true;
        }

        if (element.TryGetProperty("addresses", out var addresses) &&
            addresses.ValueKind == JsonValueKind.Array &&
            addresses.EnumerateArray().Any(candidate =>
                candidate.ValueKind == JsonValueKind.String &&
                StringComparer.OrdinalIgnoreCase.Equals(candidate.GetString(), paymentAddress)))
        {
            return true;
        }

        if (element.TryGetProperty("scriptPubKey", out var scriptPubKey))
        {
            return ContainsAddress(scriptPubKey, paymentAddress);
        }

        return false;
    }

    private static bool TryReadString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadInt32(
        JsonElement element,
        string propertyName,
        out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
    }

    private static bool TryReadPositiveDecimal(
        JsonElement element,
        string propertyName,
        out decimal value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        var parsed = property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.String when decimal.TryParse(
                property.GetString(),
                NumberStyles.Number | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out var decimalValue) => decimalValue,
            _ => 0,
        };

        value = parsed;
        return value > 0;
    }

    private static bool TryReadObservedAt(JsonElement transaction, out DateTimeOffset observedAt)
    {
        if (TryReadUnixTime(transaction, "blockTime", out observedAt))
        {
            return true;
        }

        return TryReadUnixTime(transaction, "time", out observedAt);
    }

    private static bool TryReadUnixTime(
        JsonElement element,
        string propertyName,
        out DateTimeOffset observedAt)
    {
        observedAt = default;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        var unixTime = property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var numberValue) => numberValue,
            JsonValueKind.String when long.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var stringValue) => stringValue,
            _ => 0,
        };

        if (unixTime <= 0)
        {
            return false;
        }

        observedAt = DateTimeOffset.FromUnixTimeSeconds(unixTime);
        return true;
    }

    private static string FormatCryptoAmount(decimal amount)
    {
        return amount.ToString("0.##################", CultureInfo.InvariantCulture);
    }

    private readonly record struct ChainDefinition(Uri BaseUrl, decimal AmountUnit);
}
