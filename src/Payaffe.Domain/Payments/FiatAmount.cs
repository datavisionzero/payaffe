namespace Payaffe.Domain.Payments;

public sealed record FiatAmount
{
    private static readonly ISet<string> AcceptedCurrencies = new HashSet<string>(StringComparer.Ordinal)
    {
        "EUR",
        "USD",
    };

    private FiatAmount(string currency, long minorUnits)
    {
        Currency = currency;
        MinorUnits = minorUnits;
    }

    public string Currency { get; }

    public long MinorUnits { get; }

    public static FiatAmount Create(string? currency, long minorUnits)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new DomainRuleException("Fiat Currency is required.", "fiat_currency.required");
        }

        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (!AcceptedCurrencies.Contains(normalizedCurrency))
        {
            throw new DomainRuleException(
                "Fiat Currency must be EUR or USD.",
                "fiat_currency.unsupported");
        }

        if (minorUnits <= 0)
        {
            throw new DomainRuleException(
                "Fiat Amount must be greater than zero minor units.",
                "fiat_amount.not_positive");
        }

        return new FiatAmount(normalizedCurrency, minorUnits);
    }
}
