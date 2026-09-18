using Payaffe.Application;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Auth;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Payaffe.Mcp;
using Payaffe.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Payaffe.Integration.Tests.Mcp;

public sealed class AdminMcpToolTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Guid AdminAccountId = Guid.Parse("0f2c9a1e-5f7b-4a2d-8c31-9b6de2a4f501");
    private static readonly Guid CredentialId = Guid.Parse("2b7f6c31-9d4a-4e18-b0a7-5c8e1f3d2a44");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T10:00:00Z");

    [Fact]
    public async Task Payment_search_returns_persisted_Payments()
    {
        await using var context = await BuildContextAsync();

        var result = await AdminMcpTools.SearchPaymentsAsync(context.Services, ProjectDefaults.DefaultProjectId, 25);

        Assert.Equal("resolved", result.Status);
        Assert.Equal("payments.listed", result.Code);
        var payment = Assert.Single(result.Payments);
        Assert.Equal("order-mcp-1", payment.ExternalReference);
    }

    [Fact]
    public async Task Project_list_returns_the_installation_Projects()
    {
        await using var context = await BuildContextAsync();

        var result = await AdminMcpTools.ListProjectsAsync(context.Services);

        Assert.Equal("resolved", result.Status);
        var project = Assert.Single(result.Projects);
        Assert.Equal(ProjectDefaults.DefaultProjectId, project.ProjectId);
        Assert.Equal("active", project.Status);
    }

    [Fact]
    public async Task Payment_tools_do_not_resolve_a_Payment_through_another_Project()
    {
        await using var context = await BuildContextAsync();
        var payment = Assert.Single((await AdminMcpTools.SearchPaymentsAsync(
            context.Services,
            ProjectDefaults.DefaultProjectId,
            25)).Payments);
        var otherProjectId = Guid.NewGuid();

        var search = await AdminMcpTools.SearchPaymentsAsync(context.Services, otherProjectId, 25);
        var inspect = await AdminMcpTools.InspectPaymentAsync(
            context.Services,
            otherProjectId,
            payment.PaymentId);

        Assert.Empty(search.Payments);
        Assert.Equal("payment.not_found", inspect.Code);
    }

    [Fact]
    public async Task Address_pool_summary_reports_capacity()
    {
        await using var context = await BuildContextAsync();

        var result = await AdminMcpTools.SummarizeAddressPoolAsync(context.Services, ProjectDefaults.DefaultProjectId);

        Assert.Equal("resolved", result.Status);
        Assert.Equal(0, result.Pool.UnusedCount);
        Assert.True(result.Pool.IsLowCapacity);
    }

    [Fact]
    public async Task Configuration_summary_reports_the_observation_mode()
    {
        await using var context = await BuildContextAsync();

        var result = await AdminMcpTools.SummarizeConfigurationAsync(context.Services, ProjectDefaults.DefaultProjectId);

        Assert.Equal("resolved", result.Status);
        Assert.Equal("none", result.ObservationMode);
    }

    /// <summary>
    /// The Audit Log constrains `actor_type` to `product_user` or `system`, so a
    /// denial recorded by an MCP tool has to persist as an acting Admin Account
    /// distinguished by its source service rather than by a new actor type.
    /// </summary>
    [Fact]
    public async Task Denied_risky_tool_persists_an_Audit_Log_entry_attributed_to_the_MCP_surface()
    {
        await using var context = await BuildContextAsync();

        var result = await AdminMcpTools.SettlePaymentAsync(
            context.Services,
            ProjectDefaults.DefaultProjectId,
            Guid.NewGuid(),
            expectedVersion: 1,
            reason: "operator reason",
            confirmed: false);

        Assert.Equal("confirmation.required", result.Code);

        var entry = Assert.Single(await context.ReadAuditEntriesAsync("mcp.payment.settle"));
        Assert.Equal("denied", entry.Outcome);
        Assert.Equal("confirmation.required", entry.ReasonCode);
        Assert.Equal("product_user", entry.ActorType);
        Assert.Equal("mcp", entry.SourceService);
        Assert.Equal(AdminAccountId.ToString("D"), entry.ActorId);
        Assert.Equal(ProjectDefaults.DefaultProjectId, entry.ProjectId);
        Assert.Equal(result.CorrelationId, entry.CorrelationId);
    }

    [Fact]
    public async Task Audit_log_search_records_its_own_access_through_the_MCP_surface()
    {
        await using var context = await BuildContextAsync();

        var result = await AdminMcpTools.SearchAuditLogAsync(context.Services, null, 25);

        Assert.Equal("resolved", result.Status);
        var entry = Assert.Single(await context.ReadAuditEntriesAsync("mcp.audit_log.search"));
        Assert.Equal("success", entry.Outcome);
        Assert.Equal("mcp", entry.SourceService);
    }

    [Fact]
    public async Task Settling_an_unknown_Payment_is_rejected_and_audited()
    {
        await using var context = await BuildContextAsync();

        var result = await AdminMcpTools.SettlePaymentAsync(
            context.Services,
            ProjectDefaults.DefaultProjectId,
            Guid.NewGuid(),
            expectedVersion: 1,
            reason: "operator reason",
            confirmed: true);

        Assert.Equal("payment.not_found", result.Code);
        var entry = Assert.Single(await context.ReadAuditEntriesAsync("mcp.payment.settle"));
        Assert.Equal("denied", entry.Outcome);
        Assert.Equal("payment.not_found", entry.ReasonCode);
    }

    private async Task<McpToolContext> BuildContextAsync()
    {
        var services = new ServiceCollection();
        // A real host supplies this; a bare ServiceCollection does not.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(await postgres.CreateDatabaseAsync(), registerHostedWorkers: false);
        services.AddSingleton<IPayerPageIdGenerator, FixedPayerPageIdGenerator>();
        services.Configure<PaymentApplicationOptions>(options =>
        {
            options.PayerPageBaseUrl = "https://pay.example.test/pay";
            options.PaymentExpiration = TimeSpan.FromHours(1);
            options.LateAcceptanceWindow = TimeSpan.FromHours(24);
        });
        services.AddSingleton<IOptions<AdminMcpOptions>>(Options.Create(new AdminMcpOptions
        {
            AdminAccountId = AdminAccountId,
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
        }));
        services.AddSingleton<AdminMcpRateLimiter>();

        var serviceProvider = services.BuildServiceProvider();
        await MigrationRunner.ApplyAsync(serviceProvider, CancellationToken.None);
        await SeedAsync(serviceProvider);

        return new McpToolContext(serviceProvider);
    }

    private static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        dbContext.AdminAccounts.Add(new AdminAccountRecord
        {
            Id = AdminAccountId,
            Username = "mcp-operator",
            NormalizedUsername = "mcp-operator",
            PasswordHash = "not-used-in-this-test",
            Status = "active",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        dbContext.IntegrationApiCredentials.Add(new IntegrationApiCredentialRecord
        {
            Id = CredentialId,
            Name = "MCP test credential",
            TokenHash = IntegrationApiCredentialTokenHasher.HashToken("mcp-test-token"),
            Status = "active",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await dbContext.SaveChangesAsync();

        await serviceProvider.GetRequiredService<PaymentApplicationService>().CreateAsync(
            CredentialId,
            new CreatePaymentCommand(
                "EUR",
                4999,
                "order-mcp-1",
                PaymentContext: null,
                ReturnUrl: null,
                "create-order-mcp-1"),
            CancellationToken.None);
    }

    private sealed record McpToolContext(ServiceProvider Services) : IAsyncDisposable
    {
        public async Task<IReadOnlyList<AuditLogEntryRecord>> ReadAuditEntriesAsync(string eventType)
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<PayaffeDbContext>()
                .AuditLogEntries
                .AsNoTracking()
                .Where(entry => entry.EventType == eventType)
                .ToListAsync();
        }

        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }

    private sealed class FixedPayerPageIdGenerator : IPayerPageIdGenerator
    {
        public string Generate() => "mcp-payer-page-id";
    }
}
