using Payaffe.Application.Installation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Payments;

public sealed class BlockchainObservationOptionsValidator(
    IConfiguration configuration,
    ConfiguredInstallationMode installationMode)
    : IValidateOptions<BlockchainObservationOptions>
{
    public ValidateOptionsResult Validate(string? name, BlockchainObservationOptions options)
    {
        var mode = BlockchainObservationOptions.NormalizeMode(options.Mode);
        if (installationMode.IsTest)
        {
            // Test Mode reports only Simulated Transactions. A provider here
            // would make a real transaction able to complete a simulated
            // Payment, and the other way round (ADR 0033).
            return mode == BlockchainObservationOptions.SimulatedMode
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    "BlockchainObservation:Mode must be 'simulated' or unset in Test Mode; a provider cannot be selected there.");
        }

        return mode switch
        {
            "none" => ValidateOptionsResult.Success,
            "blockchair" => ValidateProviderApiKey("Blockchair", options.Blockchair.ApiKeyReference),
            "nownodes" => ValidateProviderApiKey("NOWNodes", options.Nownodes.ApiKeyReference),
            BlockchainObservationOptions.SimulatedMode => ValidateOptionsResult.Fail(
                "BlockchainObservation:Mode 'simulated' is only available in Test Mode (Installation:Mode=test)."),
            _ => ValidateOptionsResult.Fail(
                "BlockchainObservation:Mode must be one of: none, blockchair, nownodes."),
        };
    }

    private ValidateOptionsResult ValidateProviderApiKey(
        string providerName,
        string? apiKeyReference)
    {
        if (string.IsNullOrWhiteSpace(apiKeyReference))
        {
            return ValidateOptionsResult.Fail(
                $"BlockchainObservation:{providerName}:ApiKeyReference is required when {providerName} mode is selected.");
        }

        var configurationKey = ConfigurationBlockchainObservationSecretResolver.GetConfigurationKey(apiKeyReference);
        if (configurationKey is null)
        {
            return ValidateOptionsResult.Fail(
                $"BlockchainObservation:{providerName}:ApiKeyReference must use configuration:BlockchainObservation:ProviderSecrets:<name>.");
        }

        var secret = configuration[configurationKey];
        return string.IsNullOrWhiteSpace(secret)
            ? ValidateOptionsResult.Fail(
                $"BlockchainObservation:{providerName}:ApiKeyReference points to a missing or blank provider secret.")
            : ValidateOptionsResult.Success;
    }
}
