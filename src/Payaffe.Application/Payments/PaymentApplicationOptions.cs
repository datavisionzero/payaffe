namespace Payaffe.Application.Payments;

public sealed class PaymentApplicationOptions
{
    public TimeSpan PaymentExpiration { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan LateAcceptanceWindow { get; set; } = TimeSpan.FromHours(24);

    public string PayerPageBaseUrl { get; set; } = "http://localhost/pay";

    public int BtcConfirmationRequirement { get; set; } = 1;

    public int LtcConfirmationRequirement { get; set; } = 1;

    public int EthConfirmationRequirement { get; set; } = 12;

    public int BtcReorgMonitoringDepth { get; set; } = 6;

    public int LtcReorgMonitoringDepth { get; set; } = 12;

    public int EthReorgMonitoringDepth { get; set; } = 64;

    public decimal PaymentTolerancePercent { get; set; } = 1.0m;
}
