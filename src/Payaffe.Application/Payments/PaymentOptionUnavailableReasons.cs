namespace Payaffe.Application.Payments;

/// <summary>
/// Why a Supported Currency cannot be selected. The same codes are recorded on
/// the Payment Option at creation and returned by Currency Selection, so the
/// Payer Page and the selection endpoint always name the same cause.
/// </summary>
public static class PaymentOptionUnavailableReasons
{
    public const string ExchangeRate = "exchange_rate.unavailable";
    public const string PaymentAddress = "payment_address.unavailable";
    public const string BlockchainObservation = "blockchain_observation.unavailable";
    public const string ProjectConfiguration = "project_configuration.disabled";
}
