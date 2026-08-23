namespace Payaffe.Infrastructure.Telemetry;

/// <summary>
/// Host-level observability configuration bound from the <c>Observability</c>
/// section. Every value is optional: an installation that configures none of
/// them still gets structured stdout logs, which is the documented operator
/// fallback in the observability baseline.
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>
    /// OTLP endpoint of the collector, normally Grafana Alloy. Carries traces
    /// and metrics only; logs, errors included, go to logaffe (ADR 0025,
    /// ADR 0026). When this is empty no exporter is registered and traces and
    /// metrics stay local.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>Value for the <c>deployment.environment</c> resource attribute.</summary>
    public string? DeploymentEnvironment { get; set; }

    /// <summary>
    /// Value for the <c>service.version</c> resource attribute. Falls back to
    /// the host assembly's informational version.
    /// </summary>
    public string? ServiceVersion { get; set; }

    /// <summary>Where log entries are delivered.</summary>
    public LogaffeOptions Logaffe { get; set; } = new();
}

/// <summary>
/// Delivery to a logaffe installation, which is where a host's log entries go
/// (ADR 0025). Both values are needed together: an installation is not reachable
/// without an address, and a delivery is rejected without a token — the token is
/// what names the project it lands in.
/// </summary>
public sealed class LogaffeOptions
{
    /// <summary>
    /// Scheme and host of the installation as the operator reaches it, for
    /// example <c>https://logs.example.com</c>. The ingest path is appended by
    /// the client and is not configurable. Empty disables delivery and keeps
    /// logs on stdout.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// The ingest token, which is a secret and also selects the logaffe project
    /// entries land in. Belongs in the environment, never in the repository
    /// (ADR 0021).
    /// </summary>
    public string? IngestToken { get; set; }

    /// <summary>
    /// True when neither value is set, which is the default and means an
    /// installation without central logging.
    /// </summary>
    public bool IsUnconfigured =>
        string.IsNullOrWhiteSpace(Url) && string.IsNullOrWhiteSpace(IngestToken);
}
