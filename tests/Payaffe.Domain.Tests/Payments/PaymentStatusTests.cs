using Payaffe.Domain.Payments;

namespace Payaffe.Domain.Tests.Payments;

public sealed class PaymentStatusTests
{
    [Fact]
    public void All_contains_documented_payment_statuses()
    {
        var statusValues = PaymentStatus.All.Select(status => status.Value).ToArray();

        Assert.Equal(
            [
                "pending_currency_selection",
                "waiting_for_payment",
                "observed",
                "completed",
                "expired",
                "settled",
            ],
            statusValues);
    }

    [Fact]
    public void FromValue_returns_known_status()
    {
        var status = PaymentStatus.FromValue(" pending_currency_selection ");

        Assert.Equal(PaymentStatus.PendingCurrencySelection, status);
    }

    [Fact]
    public void FromValue_rejects_unknown_status()
    {
        var exception = Assert.Throws<DomainRuleException>(() => PaymentStatus.FromValue("unpaid"));

        Assert.Equal("payment_status.unknown", exception.Code);
    }
}
