using System.ComponentModel.DataAnnotations;

namespace Payaffe.Worker;

/// <summary>
/// Liveness configuration for the worker host, bound from <c>Workers:Health</c>.
/// </summary>
/// <remarks>
/// The worker serves no HTTP endpoint, so its container healthcheck reads a
/// heartbeat file the host refreshes only while it can still reach the
/// database. That is the worker's equivalent of the API's `/health/ready`.
/// </remarks>
public sealed class WorkerHealthOptions : IValidatableObject
{
    /// <summary>
    /// Where the heartbeat is written. The default is a temporary path inside
    /// the container, which needs no writable volume.
    /// </summary>
    public string HeartbeatPath { get; set; } =
        Path.Combine(Path.GetTempPath(), "payaffe-worker-health");

    public TimeSpan ProbeInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How old the heartbeat may be before the host counts as unhealthy.
    /// </summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromSeconds(60);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(HeartbeatPath))
        {
            yield return new ValidationResult(
                "Workers:Health:HeartbeatPath is required.",
                [nameof(HeartbeatPath)]);
        }

        if (ProbeInterval < TimeSpan.FromSeconds(1) || ProbeInterval > TimeSpan.FromMinutes(5))
        {
            yield return new ValidationResult(
                "Workers:Health:ProbeInterval must be between 00:00:01 and 00:05:00.",
                [nameof(ProbeInterval)]);
        }

        // The heartbeat is only refreshed once per probe, so an equal or
        // shorter maximum age can never be satisfied.
        if (MaxAge <= ProbeInterval)
        {
            yield return new ValidationResult(
                "Workers:Health:MaxAge must be greater than Workers:Health:ProbeInterval.",
                [nameof(MaxAge)]);
        }
    }
}
