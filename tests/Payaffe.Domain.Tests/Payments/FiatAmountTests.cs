using Payaffe.Domain.Payments;

namespace Payaffe.Domain.Tests.Payments;

public sealed class FiatAmountTests
{
    [Theory]
    [InlineData("EUR")]
    [InlineData("USD")]
    [InlineData("eur")]
    public void Create_accepts_mvp_fiat_currencies(string currency)
    {
        var amount = FiatAmount.Create(currency, 1999);

        Assert.Equal(currency.ToUpperInvariant(), amount.Currency);
        Assert.Equal(1999, amount.MinorUnits);
    }

    [Fact]
    public void Create_rejects_unsupported_currency()
    {
        var exception = Assert.Throws<DomainRuleException>(() => FiatAmount.Create("CHF", 1999));

        Assert.Equal("fiat_currency.unsupported", exception.Code);
    }

    [Fact]
    public void Create_rejects_non_positive_minor_units()
    {
        var exception = Assert.Throws<DomainRuleException>(() => FiatAmount.Create("EUR", 0));

        Assert.Equal("fiat_amount.not_positive", exception.Code);
    }

    [Fact]
    public void Create_accepts_at_most_one_million_and_rejects_more()
    {
        Assert.Equal(100_000_000, FiatAmount.Create("USD", 100_000_000).MinorUnits);

        var exception = Assert.Throws<DomainRuleException>(() => FiatAmount.Create("EUR", 100_000_001));

        Assert.Equal("fiat_amount.too_large", exception.Code);
    }
}
