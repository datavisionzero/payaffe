using Payaffe.Application.Admin;
using Payaffe.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Payaffe.Mcp.Tests;

public sealed class AdminMcpBehaviorTests
{
    private static readonly Guid AdminAccountId = Guid.Parse("6f1d4d0f-0b6e-4a0e-9a6c-2f0a4d3b7c11");
    private static readonly Guid ProjectId = Guid.Parse("1b2fe0ab-b9ab-4772-97a6-0543a5a56a31");
    private static readonly string[] SampleAddresses = ["0x0000000000000000000000000000000000000001"];

    [Theory]
    [InlineData("settle")]
    [InlineData("resend")]
    [InlineData("import")]
    public async Task Risky_tools_reject_a_missing_confirmation_and_audit_the_denial(string tool)
    {
        var context = CreateContext();

        var code = await InvokeAsync(context, tool, confirmed: false);

        Assert.Equal("confirmation.required", code);
        await context.SecurityStore.Received(1).RecordSecurityAuditAsync(
            Arg.Is<AdminAuditEntry>(entry =>
                entry.Outcome == "denied" &&
                entry.ReasonCode == "confirmation.required" &&
                entry.SourceService == "mcp"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("settle")]
    [InlineData("resend")]
    [InlineData("import")]
    public async Task Risky_tools_reject_an_exhausted_rate_limit_and_audit_the_denial(string tool)
    {
        var context = CreateContext();
        Assert.True(context.RateLimiter.TryAcquire(DateTimeOffset.UtcNow));

        var code = await InvokeAsync(context, tool, confirmed: true);

        Assert.Equal("rate_limited", code);
        await context.SecurityStore.Received(1).RecordSecurityAuditAsync(
            Arg.Is<AdminAuditEntry>(entry => entry.Outcome == "denied" && entry.ReasonCode == "rate_limited"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Denied_risky_tools_are_audited_against_the_configured_Admin_Account()
    {
        var context = CreateContext();

        await AdminMcpTools.SettlePaymentAsync(
            context.Services, ProjectId, Guid.NewGuid(), 1, "operator reason", confirmed: false);

        await context.SecurityStore.Received(1).RecordSecurityAuditAsync(
            Arg.Is<AdminAuditEntry>(entry =>
                entry.ActorType == "product_user" &&
                entry.ActorId == AdminAccountId.ToString("D") &&
                entry.EventType == "mcp.payment.settle" &&
                entry.SubjectType == "payment"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Denied_risky_tools_return_a_correlation_identifier()
    {
        var context = CreateContext();

        var result = await AdminMcpTools.ResendWebhookDeliveryAsync(
            context.Services, ProjectId, Guid.NewGuid(), confirmed: false);

        Assert.False(string.IsNullOrWhiteSpace(result.CorrelationId));
        Assert.Equal("rejected", result.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Read_tools_reject_an_out_of_range_limit_without_touching_the_database(int limit)
    {
        var context = CreateContext();

        var payments = await AdminMcpTools.SearchPaymentsAsync(context.Services, ProjectId, limit);
        var deliveries = await AdminMcpTools.SearchWebhookDeliveriesAsync(context.Services, ProjectId, limit);
        var auditLog = await AdminMcpTools.SearchAuditLogAsync(context.Services, ProjectId, limit);

        Assert.Equal("validation.failed", payments.Code);
        Assert.Equal("validation.failed", deliveries.Code);
        Assert.Equal("validation.failed", auditLog.Code);
        Assert.Contains("limit", payments.Fields);
    }

    [Fact]
    public void Rate_limiter_opens_a_new_window_after_the_configured_period()
    {
        var options = Options.Create(new AdminMcpOptions
        {
            AdminAccountId = AdminAccountId,
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
        });
        var limiter = new AdminMcpRateLimiter(options);
        var now = DateTimeOffset.UtcNow;

        Assert.True(limiter.TryAcquire(now));
        Assert.False(limiter.TryAcquire(now.AddSeconds(30)));
        Assert.True(limiter.TryAcquire(now.AddMinutes(1)));
    }

    private static async Task<string> InvokeAsync(ToolContext context, string tool, bool confirmed)
    {
        return tool switch
        {
            "settle" => (await AdminMcpTools.SettlePaymentAsync(
                context.Services, ProjectId, Guid.NewGuid(), 1, "operator reason", confirmed)).Code,
            "resend" => (await AdminMcpTools.ResendWebhookDeliveryAsync(
                context.Services, ProjectId, Guid.NewGuid(), confirmed)).Code,
            _ => (await AdminMcpTools.ImportNativeEthAddressesAsync(
                context.Services, ProjectId, SampleAddresses, confirmed)).Code,
        };
    }

    /// <summary>
    /// Only the services a denied risky tool may touch are registered. If a
    /// guard ever stopped short and let a tool reach the database, resolving
    /// the missing service would fail the test instead of silently passing.
    /// </summary>
    private static ToolContext CreateContext()
    {
        var securityStore = Substitute.For<IAdminSecurityStore>();
        var options = Options.Create(new AdminMcpOptions
        {
            AdminAccountId = AdminAccountId,
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
        });
        var rateLimiter = new AdminMcpRateLimiter(options);

        var services = new ServiceCollection();
        services.AddSingleton(securityStore);
        services.AddSingleton<IOptions<AdminMcpOptions>>(options);
        services.AddSingleton(rateLimiter);

        return new ToolContext(services.BuildServiceProvider(), securityStore, rateLimiter);
    }

    private sealed record ToolContext(
        IServiceProvider Services,
        IAdminSecurityStore SecurityStore,
        AdminMcpRateLimiter RateLimiter);
}
