namespace Payaffe.Infrastructure.Telemetry;

/// <summary>
/// Host-level observability configuration bound from the <c>Observability</c>
/// section. Every value is optional: an installation without an OTLP collector
/// still gets structured stdout logs, which is the documented operator
/// fallback in the observability baseline.
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>
    /// OTLP endpoint of the collector, normally Grafana Alloy. When this is
    /// empty no exporter is registered and telemetry stays local.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>Value for the <c>deployment.environment</c> resource attribute.</summary>
    public string? DeploymentEnvironment { get; set; }

    /// <summary>
    /// Value for the <c>service.version</c> resource attribute. Falls back to
    /// the host assembly's informational version.
    /// </summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Sentry-compatible DSN for GlitchTip error reporting. GlitchTip receives
    /// error reports only; technical logs, traces, and metrics go through OTLP.
    /// </summary>
    public string? GlitchTipDsn { get; set; }
}
