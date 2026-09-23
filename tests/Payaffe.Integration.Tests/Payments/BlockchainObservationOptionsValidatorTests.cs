using Payaffe.Application.Installation;
using Payaffe.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;

namespace Payaffe.Integration.Tests.Payments;

public sealed class BlockchainObservationOptionsValidatorTests
{
    [Fact]
    public void Validate_accepts_none_mode_without_provider_secret_references()
    {
        var validator = CreateValidator(new Dictionary<string, string?>());

        var result = validator.Validate(null, new BlockchainObservationOptions
        {
            Mode = "none",
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_accepts_blockchair_mode_with_configured_provider_secret_reference()
    {
        var validator = CreateValidator(new Dictionary<string, string?>
        {
            ["BlockchainObservation:ProviderSecrets:blockchair"] = "blockchair-api-key",
        });

        var result = validator.Validate(null, new BlockchainObservationOptions
        {
            Mode = "blockchair",
            Blockchair = new BlockchainObservationProviderOptions
            {
                ApiKeyReference = "configuration:BlockchainObservation:ProviderSecrets:blockchair",
            },
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_accepts_nownodes_mode_with_configured_provider_secret_reference()
    {
        var validator = CreateValidator(new Dictionary<string, string?>
        {
            ["BlockchainObservation:ProviderSecrets:nownodes"] = "nownodes-api-key",
        });

        var result = validator.Validate(null, new BlockchainObservationOptions
        {
            Mode = "nownodes",
            Nownodes = new NownodesBlockchainObservationProviderOptions
            {
                ApiKeyReference = "configuration:BlockchainObservation:ProviderSecrets:nownodes",
            },
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_rejects_unknown_mode()
    {
        var validator = CreateValidator(new Dictionary<string, string?>());

        var result = validator.Validate(null, new BlockchainObservationOptions
        {
            Mode = "mixed",
        });

        Assert.False(result.Succeeded);
        Assert.Contains("Mode must be one of", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_selected_mode_without_api_key_reference()
    {
        var validator = CreateValidator(new Dictionary<string, string?>());

        var result = validator.Validate(null, new BlockchainObservationOptions
        {
            Mode = "blockchair",
        });

        Assert.False(result.Succeeded);
        Assert.Contains("ApiKeyReference is required", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("configuration:ConnectionStrings:Payaffe")]
    [InlineData("configuration:Webhooks:EndpointSecrets:checkout")]
    [InlineData("secret://blockchain-observation/blockchair")]
    public void Validate_rejects_provider_api_key_references_outside_allowed_configuration_subtree(
        string apiKeyReference)
    {
        var validator = CreateValidator(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Payaffe"] = "Host=db;Password=database-secret",
            ["Webhooks:EndpointSecrets:checkout"] = "webhook-secret",
        });

        var result = validator.Validate(null, new BlockchainObservationOptions
        {
            Mode = "blockchair",
            Blockchair = new BlockchainObservationProviderOptions
            {
                ApiKeyReference = apiKeyReference,
            },
        });

        Assert.False(result.Succeeded);
        Assert.Contains(
            "configuration:BlockchainObservation:ProviderSecrets:<name>",
            result.FailureMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_missing_provider_secret()
    {
        var validator = CreateValidator(new Dictionary<string, string?>
        {
            ["BlockchainObservation:ProviderSecrets:blockchair"] = "   ",
        });

        var result = validator.Validate(null, new BlockchainObservationOptions
        {
            Mode = "blockchair",
            Blockchair = new BlockchainObservationProviderOptions
            {
                ApiKeyReference = "configuration:BlockchainObservation:ProviderSecrets:blockchair",
            },
        });

        Assert.False(result.Succeeded);
        Assert.Contains("missing or blank provider secret", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_refuses_simulated_mode_in_a_live_installation()
    {
        var validator = CreateValidator(new Dictionary<string, string?>());

        var result = validator.Validate(null, new BlockchainObservationOptions { Mode = "simulated" });

        Assert.False(result.Succeeded);
        Assert.Contains("only available in Test Mode", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("simulated", true)]
    [InlineData("blockchair", false)]
    [InlineData("nownodes", false)]
    [InlineData("none", false)]
    public void Validate_accepts_only_simulated_mode_in_a_test_installation(string mode, bool accepted)
    {
        var validator = CreateValidator(
            new Dictionary<string, string?>
            {
                ["BlockchainObservation:ProviderSecrets:blockchair"] = "provider-key",
            },
            new ConfiguredInstallationMode(InstallationMode.Test));

        var result = validator.Validate(null, new BlockchainObservationOptions
        {
            Mode = mode,
            Blockchair = new BlockchainObservationProviderOptions
            {
                ApiKeyReference = "configuration:BlockchainObservation:ProviderSecrets:blockchair",
            },
        });

        Assert.Equal(accepted, result.Succeeded);
    }

    private static BlockchainObservationOptionsValidator CreateValidator(
        IReadOnlyDictionary<string, string?> values,
        ConfiguredInstallationMode? installationMode = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new BlockchainObservationOptionsValidator(
            configuration,
            installationMode ?? ConfiguredInstallationMode.Live);
    }
}
