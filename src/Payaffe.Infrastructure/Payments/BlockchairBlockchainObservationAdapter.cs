using System.Globalization;
using System.Net;
using System.Text.Json;
using Payaffe.Application.Payments;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

public sealed class BlockchairBlockchainObservationAdapter(
    HttpClient httpClient,
    IOptions<BlockchainObservationOptions> options,
    IBlockchainObservationSecretResolver secretResolver) : IBlockchainObservationAdapter
{
    private const decimal SatoshisPerCoin = 100_000_000m;
    private const decimal WeiPerEther = 1_000_000_000_000_000_000m;
    private const string ProviderName = "blockchair";
    private static readonly Uri DefaultBaseUrl = new("https://api.blockchair.com");

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
        var requestUri = BuildAddressDashboardRequestUri(chain, target.PaymentAddress, apiKey);

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var contextState = ReadContextState(document.RootElement);
        if (!TryGetTargetData(document.RootElement, target.PaymentAddress, out var targetData))
        {
            return [];
        }

        return chain switch
        {
            "bitcoin" or "litecoin" => ParseBitcoinLikeObservations(targetData, contextState),
            "ethereum" => ParseEthereumObservations(targetData, target.PaymentAddress, contextState),
            _ => throw new InvalidOperationException($"Unsupported Blockchair chain '{chain}'."),
        };
    }

    public async Task<bool> IsObservationAvailableAsync(
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        var apiKeyReference = options.Value.Blockchair.ApiKeyReference;
        return !string.IsNullOrWhiteSpace(apiKeyReference) &&
               !string.IsNullOrWhiteSpace(await secretResolver.ResolveAsync(apiKeyReference, cancellationToken));
    }

    private async Task<string> ResolveApiKeyAsync(CancellationToken cancellationToken)
    {
        var apiKeyReference = options.Value.Blockchair.ApiKeyReference;
        if (string.IsNullOrWhiteSpace(apiKeyReference))
        {
            throw new InvalidOperationException("Blockchair API key reference is not configured.");
        }

        var apiKey = await secretResolver.ResolveAsync(apiKeyReference, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Blockchair API key reference could not be resolved.");
        }

        return apiKey;
    }

    private Uri BuildAddressDashboardRequestUri(
        string chain,
        string paymentAddress,
        string apiKey)
    {
        var escapedAddress = Uri.EscapeDataString(paymentAddress);
        var baseUrl = options.Value.Blockchair.BaseUrl ?? DefaultBaseUrl;
        var query = chain == "ethereum"
            ? $"limit={GetMaxTransactionsPerAddressPoll()}&state=latest&key={Uri.EscapeDataString(apiKey)}"
            : $"limit={GetMaxTransactionsPerAddressPoll()},0&transaction_details=true&state=latest&key={Uri.EscapeDataString(apiKey)}";

        return new Uri(baseUrl, $"{chain}/dashboards/address/{escapedAddress}?{query}");
    }

    private int GetMaxTransactionsPerAddressPoll()
    {
        return Math.Clamp(options.Value.Blockchair.MaxTransactionsPerAddressPoll, 1, 100);
    }

    private static string ResolveChain(string supportedCurrency)
    {
        return supportedCurrency.ToUpperInvariant() switch
        {
            "BTC" => "bitcoin",
            "LTC" => "litecoin",
            "ETH" => "ethereum",
            _ => throw new InvalidOperationException(
                $"Unsupported Blockchair Blockchain Observation currency '{supportedCurrency}'."),
        };
    }

    private static int ReadContextState(JsonElement root)
    {
        if (root.TryGetProperty("context", out var context) &&
            context.TryGetProperty("state", out var state) &&
            state.TryGetInt32(out var stateValue))
        {
            return stateValue;
        }

        return 0;
    }

    private static bool TryGetTargetData(
        JsonElement root,
        string paymentAddress,
        out JsonElement targetData)
    {
        targetData = default;
        if (!root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (data.TryGetProperty(paymentAddress, out targetData))
        {
            return true;
        }

        foreach (var property in data.EnumerateObject())
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(property.Name, paymentAddress))
            {
                targetData = property.Value;
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<BlockchainObservation> ParseBitcoinLikeObservations(
        JsonElement targetData,
        int contextState)
    {
        if (!targetData.TryGetProperty("transactions", out var transactions) ||
            transactions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var observations = new List<BlockchainObservation>();
        foreach (var transaction in transactions.EnumerateArray())
        {
            if (!TryReadString(transaction, "hash", out var transactionHash) ||
                !TryReadPositiveDecimal(transaction, "balance_change", out var satoshis) ||
                !TryReadObservedAt(transaction, "time", out var observedAt))
            {
                continue;
            }

            var blockId = TryReadInt32(transaction, "block_id", out var readBlockId)
                ? readBlockId
                : -1;
            observations.Add(new BlockchainObservation(
                transactionHash,
                FormatCryptoAmount(satoshis / SatoshisPerCoin),
                observedAt,
                CalculateConfirmations(blockId, contextState),
                ProviderName,
                $"blockchair:{transactionHash}",
                BlockHash: null,
                BlockHeight: blockId >= 0 ? blockId : null));
        }

        return observations;
    }

    private static IReadOnlyList<BlockchainObservation> ParseEthereumObservations(
        JsonElement targetData,
        string paymentAddress,
        int contextState)
    {
        if (!targetData.TryGetProperty("calls", out var calls) ||
            calls.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var observations = new List<BlockchainObservation>();
        foreach (var call in calls.EnumerateArray())
        {
            if (!TryReadString(call, "transaction_hash", out var transactionHash) ||
                !TryReadString(call, "recipient", out var recipient) ||
                !StringComparer.OrdinalIgnoreCase.Equals(recipient, paymentAddress) ||
                !TryReadPositiveDecimal(call, "value", out var wei) ||
                !TryReadObservedAt(call, "time", out var observedAt) ||
                !TryReadTransferred(call))
            {
                continue;
            }

            var blockId = TryReadInt32(call, "block_id", out var readBlockId)
                ? readBlockId
                : -1;
            observations.Add(new BlockchainObservation(
                transactionHash,
                FormatCryptoAmount(wei / WeiPerEther),
                observedAt,
                CalculateConfirmations(blockId, contextState),
                ProviderName,
                $"blockchair:{transactionHash}",
                BlockHash: null,
                BlockHeight: blockId >= 0 ? blockId : null));
        }

        return observations;
    }

    private static bool TryReadTransferred(JsonElement element)
    {
        return !element.TryGetProperty("transferred", out var transferred) ||
               transferred.ValueKind != JsonValueKind.False;
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

    private static bool TryReadObservedAt(
        JsonElement element,
        string propertyName,
        out DateTimeOffset observedAt)
    {
        observedAt = default;
        return TryReadString(element, propertyName, out var time) &&
               DateTimeOffset.TryParse(
                   time,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                   out observedAt);
    }

    private static int CalculateConfirmations(int blockId, int contextState)
    {
        if (blockId < 0 || contextState <= 0 || contextState < blockId)
        {
            return 0;
        }

        return contextState - blockId + 1;
    }

    private static string FormatCryptoAmount(decimal amount)
    {
        return amount.ToString("0.##################", CultureInfo.InvariantCulture);
    }
}
