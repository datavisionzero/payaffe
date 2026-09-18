namespace Payaffe.Application.Payments;

public interface IProjectPaymentConfigurationStore
{
    Task<ProjectPaymentConfiguration?> FindByCredentialAsync(
        Guid integrationApiCredentialId,
        CancellationToken cancellationToken);

    Task<ProjectPaymentConfiguration?> FindByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken);
}

public sealed record ProjectPaymentConfiguration(
    Guid ProjectId,
    string ProjectStatus,
    TimeSpan PaymentExpiration,
    TimeSpan LateAcceptanceWindow,
    decimal PaymentTolerancePercent,
    ProjectCurrencyConfiguration Btc,
    ProjectCurrencyConfiguration Ltc,
    ProjectCurrencyConfiguration Eth)
{
    public ProjectCurrencyConfiguration For(string supportedCurrency) =>
        supportedCurrency switch
        {
            "BTC" => Btc,
            "LTC" => Ltc,
            "ETH" => Eth,
            _ => ProjectCurrencyConfiguration.Disabled,
        };
}

public sealed record ProjectCurrencyConfiguration(
    bool Enabled,
    int ConfirmationRequirement,
    int ReorgMonitoringDepth)
{
    public static ProjectCurrencyConfiguration Disabled { get; } = new(false, 0, 0);
}
