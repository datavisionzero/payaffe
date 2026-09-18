using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payaffe.Application.Admin;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Sdk;

namespace Payaffe.Api.Tests.Payments;

/// <summary>
/// The consumer scenarios of the embedded payment baseline, driven through the packaged SDK
/// against a server test host. Each test is one row of that document's acceptance table, so a
/// behaviour an integrator was told to rely on cannot change unnoticed.
/// </summary>
public sealed class PayaffeSdkConsumerScenarioTests
{
    private const string _token = "sdk-scenario-token";

    [Fact]
    public async Task An_unavailable_currency_keeps_the_other_options_selectable()
    {
        await using PaymentApiFactory factory = new();
        factory.UnavailableRateCurrencies.Add("LTC");
        factory.UnavailableAddressCurrencies.Add("ETH");
        PayaffeClient payaffe = await CreateClientAsync(factory);

        Payment created = await payaffe.CreatePaymentAsync(
            NewOrder("unavailable-option"),
            "unavailable-option");

        PaymentOption litecoin = OptionFor(created, SupportedCurrency.Ltc);
        PaymentOption ether = OptionFor(created, SupportedCurrency.Eth);
        Assert.Equal(PaymentOptionStatus.Unavailable, litecoin.Status);
        Assert.Equal("exchange_rate.unavailable", litecoin.UnavailableReasonCode);
        Assert.Equal(PaymentOptionStatus.Unavailable, ether.Status);
        Assert.Equal("payment_address.unavailable", ether.UnavailableReasonCode);
        Assert.Equal(PaymentOptionStatus.Available, OptionFor(created, SupportedCurrency.Btc).Status);

        // No amount or address is manufactured for the options that cannot be offered.
        Assert.Null(created.ExpectedCryptoAmount);
        Assert.Null(created.PaymentAddress);

        Payment selected = await payaffe.SelectCurrencyAsync(created.PaymentId, SupportedCurrency.Btc);
        Assert.Equal(PaymentStatus.WaitingForPayment, selected.Status);
    }

    [Fact]
    public async Task An_unavailable_rate_during_selection_retains_the_payment_for_a_later_retry()
    {
        await using PaymentApiFactory factory = new();
        PayaffeClient payaffe = await CreateClientAsync(factory);
        Payment created = await payaffe.CreatePaymentAsync(
            NewOrder("rate-unavailable"),
            "rate-unavailable");
        factory.UnavailableRateCurrencies.Add("BTC");

        PayaffeApiException failure = await Assert.ThrowsAsync<PayaffeApiException>(() =>
            payaffe.SelectCurrencyAsync(created.PaymentId, SupportedCurrency.Btc));

        Assert.Equal(HttpStatusCode.Conflict, failure.StatusCode);
        Assert.Equal("exchange_rate.unavailable", failure.Code.Value);
        Assert.DoesNotContain(_token, failure.Message, StringComparison.Ordinal);

        Payment refreshed = await payaffe.GetPaymentAsync(created.PaymentId);
        Assert.Equal(PaymentStatus.PendingCurrencySelection, refreshed.Status);
        Assert.Null(refreshed.RateLock);
        Assert.Null(refreshed.PaymentInstruction);

        factory.UnavailableRateCurrencies.Remove("BTC");
        Payment selected = await payaffe.SelectCurrencyAsync(created.PaymentId, SupportedCurrency.Btc);
        Assert.Equal(PaymentStatus.WaitingForPayment, selected.Status);
        Assert.NotNull(selected.PaymentInstruction);
    }

    [Fact]
    public async Task A_repeated_creation_with_the_original_key_stays_one_payment()
    {
        await using PaymentApiFactory factory = new();
        PayaffeClient payaffe = await CreateClientAsync(factory);
        CreatePaymentRequest request = NewOrder("ambiguous-create");

        Payment first = await payaffe.CreatePaymentAsync(request, "ambiguous-create");
        Payment replayed = await payaffe.CreatePaymentAsync(request, "ambiguous-create");

        Assert.Equal(first.PaymentId, replayed.PaymentId);
        Assert.Equal(first.CreatedAt, replayed.CreatedAt);

        // A different body under the same key is a caller defect, not a second Payment.
        PayaffeApiException conflict = await Assert.ThrowsAsync<PayaffeApiException>(() =>
            payaffe.CreatePaymentAsync(
                request with { FiatAmountMinor = 2999 },
                "ambiguous-create"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("idempotency.conflict", conflict.Code.Value);
    }

    [Fact]
    public async Task A_repeated_selection_returns_the_same_immutable_instruction()
    {
        await using PaymentApiFactory factory = new();
        PayaffeClient payaffe = await CreateClientAsync(factory);
        Payment created = await payaffe.CreatePaymentAsync(
            NewOrder("ambiguous-selection"),
            "ambiguous-selection");

        Payment first = await payaffe.SelectCurrencyAsync(created.PaymentId, SupportedCurrency.Btc);
        Payment repeated = await payaffe.SelectCurrencyAsync(created.PaymentId, SupportedCurrency.Btc);

        Assert.Equal(first.PaymentInstruction, repeated.PaymentInstruction);
        Assert.Equal(first.RateLock, repeated.RateLock);
    }

    [Fact]
    public async Task The_first_committed_currency_wins_and_the_loser_must_display_it()
    {
        await using PaymentApiFactory factory = new();
        PayaffeClient payaffe = await CreateClientAsync(factory);
        Payment created = await payaffe.CreatePaymentAsync(
            NewOrder("currency-race"),
            "currency-race");

        Payment winner = await payaffe.SelectCurrencyAsync(created.PaymentId, SupportedCurrency.Btc);
        PayaffeApiException loser = await Assert.ThrowsAsync<PayaffeApiException>(() =>
            payaffe.SelectCurrencyAsync(created.PaymentId, SupportedCurrency.Ltc));

        Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
        Assert.Equal("payment.currency_already_selected", loser.Code.Value);

        Payment read = await payaffe.GetPaymentAsync(created.PaymentId);
        Assert.Equal(SupportedCurrency.Btc, read.SelectedCurrency);
        Assert.Equal(winner.PaymentInstruction, read.PaymentInstruction);
    }

    [Fact]
    public async Task An_expired_payment_is_not_selectable_and_allocates_nothing()
    {
        await using PaymentApiFactory factory = new();
        PayaffeClient payaffe = await CreateClientAsync(factory);
        Payment created = await payaffe.CreatePaymentAsync(
            NewOrder("expired-selection"),
            "expired-selection");
        await factory.ExpirePaymentAsync(created.PaymentId);

        PayaffeApiException failure = await Assert.ThrowsAsync<PayaffeApiException>(() =>
            payaffe.SelectCurrencyAsync(created.PaymentId, SupportedCurrency.Btc));

        Assert.Equal(HttpStatusCode.Conflict, failure.StatusCode);
        Assert.Equal("payment.expired", failure.Code.Value);

        Payment read = await payaffe.GetPaymentAsync(created.PaymentId);
        Assert.Null(read.SelectedCurrency);
        Assert.Null(read.RateLock);
        Assert.Null(read.PaymentAddress);
    }

    [Fact]
    public async Task An_underpayment_is_reported_with_its_totals_and_awaits_resolution()
    {
        await using PaymentApiFactory factory = new();
        PayaffeClient payaffe = await CreateClientAsync(factory);
        Payment created = await payaffe.CreatePaymentAsync(
            NewOrder("underpaid-order"),
            "underpaid-order");
        Payment selected = await payaffe.SelectCurrencyAsync(created.PaymentId, SupportedCurrency.Btc);
        await ObserveAsync(factory, selected, "0.00020000", "underpaid");

        Payment observed = await payaffe.GetPaymentAsync(created.PaymentId);

        Assert.Equal(PaymentStatus.Observed, observed.Status);
        Assert.Equal("underpaid", observed.ObservedAmountState);
        Assert.Equal("0.0002", observed.ObservedTotal);
        Assert.Equal("0.00039980", observed.ExpectedCryptoAmount);
        Assert.False(observed.Status.IsTerminal);
        Assert.Null(observed.CompletedAt);
    }

    [Fact]
    public async Task An_overpayment_completes_and_keeps_its_amount_state_for_reconciliation()
    {
        await using PaymentApiFactory factory = new();
        PayaffeClient payaffe = await CreateClientAsync(factory);
        Payment created = await payaffe.CreatePaymentAsync(
            NewOrder("overpaid-order"),
            "overpaid-order");
        Payment selected = await payaffe.SelectCurrencyAsync(created.PaymentId, SupportedCurrency.Btc);
        await ObserveAsync(factory, selected, "0.00050000", "overpaid");

        Payment completed = await payaffe.GetPaymentAsync(created.PaymentId);

        Assert.Equal(PaymentStatus.Completed, completed.Status);
        Assert.Equal("overpaid", completed.ObservedAmountState);
        Assert.Equal("0.0005", completed.ObservedTotal);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task An_expired_payment_stays_readable_until_a_settlement_resolves_it()
    {
        await using PaymentApiFactory factory = new();
        PayaffeClient payaffe = await CreateClientAsync(factory);
        Payment created = await payaffe.CreatePaymentAsync(
            NewOrder("late-payment"),
            "late-payment");
        Payment selected = await payaffe.SelectCurrencyAsync(created.PaymentId, SupportedCurrency.Btc);
        await ObserveAsync(factory, selected, "0.00020000", "late");
        await EndLateAcceptanceAsync(factory, created.PaymentId);

        Payment expired = await payaffe.GetPaymentAsync(created.PaymentId);
        Assert.Equal(PaymentStatus.Expired, expired.Status);
        Assert.True(expired.Status.IsTerminal);

        // `expired` is terminal but not a failure: the Payment may still be resolved, and a
        // consumer must not infer either outcome before Payaffe reports it.
        Assert.Null(expired.CompletedAt);
        Assert.Null(expired.SettledAt);

        await SettleAsync(factory, created.PaymentId);

        Payment settled = await payaffe.GetPaymentAsync(created.PaymentId);
        Assert.Equal(PaymentStatus.Settled, settled.Status);
        Assert.NotNull(settled.SettledAt);
    }

    private static async Task<PayaffeClient> CreateClientAsync(PaymentApiFactory factory)
    {
        await factory.SeedCredentialAsync(_token);
        return new PayaffeClient(factory.CreateClient(), new Uri("http://localhost"), _token);
    }

    private static CreatePaymentRequest NewOrder(string externalReference) =>
        new("EUR", 1999, externalReference);

    private static PaymentOption OptionFor(Payment payment, SupportedCurrency currency) =>
        payment.PaymentOptions.Single(option => option.SupportedCurrency == currency);

    private static async Task ObserveAsync(
        PaymentApiFactory factory,
        Payment selected,
        string observedAmount,
        string transactionSuffix)
    {
        PaymentInstruction instruction = selected.PaymentInstruction!;
        using IServiceScope scope = factory.Services.CreateScope();
        PaymentApplicationService payments = scope.ServiceProvider
            .GetRequiredService<PaymentApplicationService>();
        RecordBlockchainObservationResult result = await payments.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                selected.PaymentId,
                instruction.SupportedCurrency.Value,
                instruction.PaymentAddress,
                $"transaction-{transactionSuffix}",
                observedAmount,
                DateTimeOffset.UtcNow,
                Confirmations: 1,
                "controlled-test-provider",
                $"observation-{transactionSuffix}"),
            CancellationToken.None);
        Assert.NotEqual(RecordBlockchainObservationResultKind.PaymentNotReady, result.Kind);
        Assert.NotEqual(RecordBlockchainObservationResultKind.PaymentNotFound, result.Kind);
    }

    private static async Task EndLateAcceptanceAsync(PaymentApiFactory factory, Guid paymentId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        PayaffeDbContext dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        PaymentRecord record = await dbContext.Payments
            .SingleAsync(payment => payment.Id == paymentId);
        record.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-25);
        record.LateAcceptanceEndsAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await dbContext.SaveChangesAsync();

        PaymentApplicationService payments = scope.ServiceProvider
            .GetRequiredService<PaymentApplicationService>();
        ExpireDuePaymentsResult expiration = await payments.ExpireDuePaymentsAsync(
            10,
            CancellationToken.None);
        Assert.Equal(1, expiration.ExpiredCount);
    }

    private static async Task SettleAsync(PaymentApiFactory factory, Guid paymentId)
    {
        Guid adminAccountId = await factory.SeedAdminAccountAsync(
            "settlement-admin",
            "settlement-admin-password");
        using IServiceScope scope = factory.Services.CreateScope();
        PayaffeDbContext dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        long version = await dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.Id == paymentId)
            .Select(payment => payment.Version)
            .SingleAsync();

        AdminPaymentQueryService admin = scope.ServiceProvider
            .GetRequiredService<AdminPaymentQueryService>();
        AdminPaymentSettlementResult result = await admin.SettleAsync(
            ProjectDefaults.DefaultProjectId,
            paymentId,
            version,
            "Payer confirmed the transfer out of band.",
            new AdminOperationContext(
                adminAccountId,
                SourceIp: null,
                UserAgent: null,
                Guid.NewGuid().ToString("D")),
            CancellationToken.None);

        Assert.Equal(AdminPaymentSettlementResultKind.Settled, result.Kind);
    }
}
