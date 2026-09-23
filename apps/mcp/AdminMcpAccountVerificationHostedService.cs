using Payaffe.Application.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Payaffe.Mcp;

/// <summary>
/// Refuses to start when <c>Mcp:Admin:AdminAccountId</c> names no Admin
/// Account or a disabled one, so a misconfigured host fails at start rather
/// than on the first tool call.
/// </summary>
/// <remarks>
/// A database that cannot be reached is not a refusal, as for the Installation
/// Mode check: the host could do nothing against it either, and every tool call
/// checks the account again before it acts.
/// </remarks>
public sealed class AdminMcpAccountVerificationHostedService(
    IServiceProvider serviceProvider,
    IOptions<AdminMcpOptions> options,
    ILogger<AdminMcpAccountVerificationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var accountId = options.Value.AdminAccountId;
        string? status;
        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            status = await scope.ServiceProvider.GetRequiredService<IAdminSecurityStore>()
                .FindAccountStatusAsync(accountId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "The configured Admin Account could not be checked; the product database is not reachable yet.");
            return;
        }

        if (status != "active")
        {
            throw new AdminMcpAccountUnavailableException(
                status is null
                    ? "Mcp:Admin:AdminAccountId does not name an existing Admin Account."
                    : "Mcp:Admin:AdminAccountId names a disabled Admin Account.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class AdminMcpAccountUnavailableException(string message) : Exception(message);
