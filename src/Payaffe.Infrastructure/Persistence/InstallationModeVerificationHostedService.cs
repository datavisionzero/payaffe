using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Payaffe.Infrastructure.Persistence;

/// <summary>
/// Refuses to start a host that does not apply the schema itself, such as the
/// local Admin MCP host, when its configured Installation Mode differs from
/// the recorded one.
/// </summary>
/// <remarks>
/// A database that cannot be reached is not a refusal. Such a host has nothing
/// it could do against it either, and it reports that on the first call
/// instead of failing to start for a reason that is not about the mode.
/// </remarks>
public sealed class InstallationModeVerificationHostedService(
    IServiceProvider serviceProvider,
    ILogger<InstallationModeVerificationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        try
        {
            await InstallationModeRecord.VerifyAsync(scope.ServiceProvider, cancellationToken);
        }
        catch (Exception exception) when (exception is not (InstallationModeMismatchException or OperationCanceledException))
        {
            logger.LogWarning(
                exception,
                "The recorded Installation Mode could not be read; the product database is not reachable yet.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
