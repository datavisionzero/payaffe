using System.ComponentModel.DataAnnotations;

namespace Payaffe.Infrastructure.Telemetry;

public sealed class OperationalMetricsOptions : IValidatableObject
{
    /// <summary>
    /// Snapshots are only useful in a host that also runs the workers, so this
    /// follows the worker registration rather than a separate default.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromSeconds(30);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SnapshotInterval < TimeSpan.FromSeconds(5) || SnapshotInterval > TimeSpan.FromMinutes(15))
        {
            yield return new ValidationResult(
                "Observability:OperationalMetrics:SnapshotInterval must be between 00:00:05 and 00:15:00.",
                [nameof(SnapshotInterval)]);
        }
    }
}
