using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Payaffe.Application.Admin;
using Payaffe.Application.Payments;
using Payaffe.Application.Webhooks;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;

namespace Payaffe.Api.Tests.Payments;

public sealed class PaymentApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"payaffe-api-tests-{Guid.NewGuid()}";
    private readonly Dictionary<string, byte[]> _totpSecrets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _webhookSecrets = new(StringComparer.Ordinal);

    public int? AdminRateLimitPermitLimit { get; set; }

    public TimeSpan? AdminRateLimitWindow { get; set; }

    public int? IntegrationApiRateLimitPermitLimit { get; set; }

    public TimeSpan? IntegrationApiRateLimitWindow { get; set; }

    public string? StaticWebRootPath { get; set; }

    /// <summary>
    /// Runs the API as a Test Mode installation, with the simulated address,
    /// rate and observation sources in place of the fixed test doubles.
    /// </summary>
    public bool TestMode { get; set; }

    public HashSet<string> UnavailableRateCurrencies { get; } = new(StringComparer.Ordinal);

    public HashSet<string> UnavailableAddressCurrencies { get; } = new(StringComparer.Ordinal);

    public HashSet<string> UnavailableObservationCurrencies { get; } = new(StringComparer.Ordinal);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Installation:Mode", TestMode ? "test" : "live");
        if (StaticWebRootPath is not null)
        {
            builder.UseWebRoot(StaticWebRootPath);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveStartupSchemaMigration();
            services.RemoveAll<DbContextOptions<PayaffeDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PayaffeDbContext>>();
            services.AddDbContext<PayaffeDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            services.Configure<PaymentApplicationOptions>(options =>
            {
                options.PayerPageBaseUrl = "https://pay.example.test/pay";
                options.PaymentExpiration = TimeSpan.FromHours(1);
                options.LateAcceptanceWindow = TimeSpan.FromHours(24);
            });
            services.Configure<RateCacheRefreshWorkerOptions>(options => options.Enabled = false);
            services.Configure<AdminAuthenticationOptions>(options =>
            {
                if (AdminRateLimitPermitLimit is not null)
                {
                    options.RateLimitPermitLimit = AdminRateLimitPermitLimit.Value;
                }

                if (AdminRateLimitWindow is not null)
                {
                    options.RateLimitWindow = AdminRateLimitWindow.Value;
                }
            });
            services.Configure<IntegrationApiRateLimitOptions>(options =>
            {
                if (IntegrationApiRateLimitPermitLimit is not null)
                {
                    options.PermitLimit = IntegrationApiRateLimitPermitLimit.Value;
                }

                if (IntegrationApiRateLimitWindow is not null)
                {
                    options.Window = IntegrationApiRateLimitWindow.Value;
                }
            });
            if (!TestMode)
            {
                services.RemoveAll<IExchangeRateSource>();
                services.RemoveAll<IPaymentAddressProvider>();
                services.RemoveAll<IBlockchainObservationAdapter>();
                services.AddScoped<IExchangeRateSource>(_ =>
                    new FixedExchangeRateSource(UnavailableRateCurrencies));
                services.AddScoped<IPaymentAddressProvider>(_ =>
                    new FixedPaymentAddressProvider(UnavailableAddressCurrencies));
                services.AddScoped<IBlockchainObservationAdapter>(_ =>
                    new FixedBlockchainObservationAdapter(UnavailableObservationCurrencies));
            }

            services.RemoveAll<IAdminTotpSecretResolver>();
            services.RemoveAll<IWebhookSecretResolver>();
            services.AddSingleton<IAdminTotpSecretResolver>(new FixedAdminTotpSecretResolver(_totpSecrets));
            services.AddSingleton<IWebhookSecretResolver>(new FixedWebhookSecretResolver(_webhookSecrets));
        });
    }

    public void AddWebhookSecret(string secretReference, string secret)
    {
        _webhookSecrets[secretReference] = secret;
    }

    public async Task<Guid> SeedCredentialAsync(
        string token,
        string status = "active",
        Guid? projectId = null)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var now = DateTimeOffset.UtcNow;
        var credentialId = Guid.NewGuid();
        var owningProjectId = projectId ?? ProjectDefaults.DefaultProjectId;

        if (!await dbContext.Projects.AnyAsync(project => project.Id == owningProjectId))
        {
            dbContext.Projects.Add(new ProjectRecord
            {
                Id = owningProjectId,
                Name = owningProjectId == ProjectDefaults.DefaultProjectId ? "Default Project" : "Test Project",
                Slug = owningProjectId == ProjectDefaults.DefaultProjectId ? ProjectDefaults.DefaultProjectSlug : owningProjectId.ToString("N"),
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        if (!await dbContext.ProjectConfigurations.AnyAsync(configuration => configuration.ProjectId == owningProjectId))
        {
            dbContext.ProjectConfigurations.Add(CreateProjectConfiguration(owningProjectId, now));
        }

        dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
        {
            ProjectId = owningProjectId,
            Id = credentialId,
            Name = "Test credential",
            TokenHash = IntegrationApiCredentialTokenHasher.HashToken(token),
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await dbContext.SaveChangesAsync();

        return credentialId;
    }

    public async Task<Guid> SeedProjectAsync(string status = "active")
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        dbContext.Projects.Add(new ProjectRecord
        {
            Id = projectId,
            Name = "Test Project",
            Slug = projectId.ToString("N"),
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
        });
        dbContext.ProjectConfigurations.Add(CreateProjectConfiguration(projectId, now));
        await dbContext.SaveChangesAsync();
        return projectId;
    }

    public async Task SetProjectStatusAsync(Guid projectId, string status)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var project = await dbContext.Projects.SingleAsync(candidate => candidate.Id == projectId);
        project.Status = status;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    public async Task SetCurrencyEnabledAsync(Guid projectId, string supportedCurrency, bool enabled)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var configuration = await dbContext.ProjectConfigurations
            .SingleAsync(candidate => candidate.ProjectId == projectId);
        switch (supportedCurrency)
        {
            case "BTC":
                configuration.BtcEnabled = enabled;
                break;
            case "LTC":
                configuration.LtcEnabled = enabled;
                break;
            case "ETH":
                configuration.EthEnabled = enabled;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(supportedCurrency));
        }

        configuration.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    public async Task ExpirePaymentAsync(Guid paymentId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var payment = await dbContext.Payments.SingleAsync(candidate => candidate.Id == paymentId);
        payment.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await dbContext.SaveChangesAsync();
    }

    private static ProjectConfigurationRecord CreateProjectConfiguration(
        Guid projectId,
        DateTimeOffset now) => new()
        {
            ProjectId = projectId,
            PaymentExpirationSeconds = 3600,
            LateAcceptanceWindowSeconds = 86400,
            PaymentTolerancePercent = 1m,
            BtcEnabled = true,
            LtcEnabled = true,
            EthEnabled = true,
            BtcConfirmationRequirement = 1,
            LtcConfirmationRequirement = 1,
            EthConfirmationRequirement = 12,
            BtcReorgMonitoringDepth = 6,
            LtcReorgMonitoringDepth = 12,
            EthReorgMonitoringDepth = 64,
            NativeEthLowCapacityThreshold = 20,
            LegacySettingsFingerprint = string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public async Task<Guid> SeedAdminAccountAsync(
        string username,
        string password,
        string status = "active",
        string? totpSecretReference = "secret-ref:test-admin",
        byte[]? totpSecret = null)
    {
        if (totpSecretReference is not null && totpSecret is not null)
        {
            _totpSecrets[totpSecretReference] = totpSecret;
        }

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var now = DateTimeOffset.UtcNow;
        var adminAccountId = Guid.NewGuid();
        var passwordHasher = new AdminPasswordHasher();

        if (!await dbContext.Projects.AnyAsync(project => project.Id == ProjectDefaults.DefaultProjectId))
        {
            dbContext.Projects.Add(new ProjectRecord
            {
                Id = ProjectDefaults.DefaultProjectId,
                Name = "Default Project",
                Slug = ProjectDefaults.DefaultProjectSlug,
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        dbContext.AdminAccounts.Add(new AdminAccountRecord
        {
            Id = adminAccountId,
            Username = username,
            NormalizedUsername = username.Trim().ToUpperInvariant(),
            PasswordHash = passwordHasher.HashPassword(password),
            TotpSecretReference = totpSecretReference,
            Status = status,
            FailedPasswordAttemptCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await dbContext.SaveChangesAsync();

        return adminAccountId;
    }

    private sealed class FixedExchangeRateSource(IReadOnlySet<string> unavailableCurrencies)
        : IExchangeRateSource
    {
        public Task<RateLockQuote?> GetRateLockQuoteAsync(
            string fiatCurrency,
            long fiatAmountMinor,
            string supportedCurrency,
            DateTimeOffset requestedAt,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                unavailableCurrencies.Contains(supportedCurrency)
                    ? null
                    : new RateLockQuote(
                        supportedCurrency,
                        "test-rate-source",
                        "50000.00",
                        "0.00039980",
                        requestedAt));
        }

        public Task<bool> IsRateAvailableAsync(
            string fiatCurrency,
            string supportedCurrency,
            DateTimeOffset checkedAt,
            CancellationToken cancellationToken) =>
            Task.FromResult(!unavailableCurrencies.Contains(supportedCurrency));
    }

    private sealed class FixedPaymentAddressProvider(IReadOnlySet<string> unavailableCurrencies)
        : IPaymentAddressProvider
    {
        public Task<PaymentAddressAssignment?> AssignAsync(
            Guid projectId,
            Guid paymentId,
            string supportedCurrency,
            CancellationToken cancellationToken)
        {
            var assignment = supportedCurrency switch
            {
                "BTC" => new PaymentAddressAssignment(
                    "BTC",
                    "bc1qpayaffetestaddress0000000000000000000000000",
                    "mainnet"),
                "LTC" => new PaymentAddressAssignment(
                    "LTC",
                    "ltc1qpayaffetestaddress000000000000000000000000",
                    "mainnet"),
                "ETH" => new PaymentAddressAssignment(
                    "ETH",
                    "0x1111111111111111111111111111111111111111",
                    "mainnet",
                    1),
                _ => throw new ArgumentOutOfRangeException(nameof(supportedCurrency)),
            };
            return Task.FromResult(
                unavailableCurrencies.Contains(supportedCurrency)
                    ? null
                    : assignment);
        }

        public Task<bool> IsAddressAvailableAsync(
            Guid projectId,
            string supportedCurrency,
            CancellationToken cancellationToken) =>
            Task.FromResult(!unavailableCurrencies.Contains(supportedCurrency));
    }

    private sealed class FixedBlockchainObservationAdapter(
        IReadOnlySet<string> unavailableCurrencies) : IBlockchainObservationAdapter
    {
        public Task StartWatchingAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<BlockchainObservation>> PollAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BlockchainObservation>>([]);

        public Task<bool> IsObservationAvailableAsync(
            string supportedCurrency,
            CancellationToken cancellationToken) =>
            Task.FromResult(!unavailableCurrencies.Contains(supportedCurrency));
    }

    private sealed class FixedAdminTotpSecretResolver(
        IReadOnlyDictionary<string, byte[]> secrets) : IAdminTotpSecretResolver
    {
        public Task<byte[]?> ResolveSecretAsync(
            string secretReference,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(secrets.TryGetValue(secretReference, out var secret) ? secret : null);
        }
    }

    private sealed class FixedWebhookSecretResolver(
        IReadOnlyDictionary<string, string> secrets) : IWebhookSecretResolver
    {
        public Task<string?> ResolveForProjectAsync(
            Guid projectId,
            string secretReference,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(secrets.TryGetValue(secretReference, out var secret) ? secret : null);
        }
    }
}
