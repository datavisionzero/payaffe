using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Payaffe.Infrastructure.Telemetry;

/// <summary>
/// The product-owned <see cref="ActivitySource"/> and <see cref="Meter"/> for
/// technical telemetry. Instrumentation stays on the .NET APIs so hosts can
/// choose an exporter, or none at all, without changing product code.
/// </summary>
/// <remarks>
/// Only technical, non-personal values are recorded here. Payment identifiers,
/// Payment Context Fields, addresses, secrets, and provider payloads stay out
/// of metric attributes so telemetry cannot become a second, unaudited copy of
/// product data.
/// </remarks>
public static class PayaffeTelemetry
{
    public const string ServiceNamespace = "payaffe";
    public const string MeterName = "Payaffe";
    public const string ActivitySourceName = "Payaffe";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> WorkerRuns = Meter.CreateCounter<long>(
        "payaffe.worker.runs",
        unit: "{run}",
        description: "Background worker batches by worker name and outcome.");

    private static readonly Counter<long> WebhookAttempts = Meter.CreateCounter<long>(
        "payaffe.webhook.delivery.attempts",
        unit: "{attempt}",
        description: "Webhook Delivery attempts by result.");

    private static readonly Counter<long> WebhookTerminalTransitions = Meter.CreateCounter<long>(
        "payaffe.webhook.delivery.terminal",
        unit: "{event}",
        description: "Webhook Outbox events that became terminal failed.");

    private static readonly Gauge<long> WorkerConsecutiveFailures = Meter.CreateGauge<long>(
        "payaffe.worker.consecutive_failures",
        unit: "{failure}",
        description: "Consecutive failed batches per named worker lease.");

    private static readonly Gauge<long> WebhookTerminalFailures = Meter.CreateGauge<long>(
        "payaffe.webhook.terminal_failures",
        unit: "{event}",
        description: "Webhook Outbox events in a terminal failed state.");

    private static readonly Gauge<long> ObservationUnavailable = Meter.CreateGauge<long>(
        "payaffe.observation.unavailable",
        unit: "{currency}",
        description: "Supported Currencies whose Blockchain Observation is unavailable.");

    private static readonly Gauge<long> AddressPoolAvailable = Meter.CreateGauge<long>(
        "payaffe.address_pool.available",
        unit: "{address}",
        description: "Unassigned addresses left in the native ETH Address Pool.");

    private static readonly Gauge<long> OpenReorgAlerts = Meter.CreateGauge<long>(
        "payaffe.reorg_alerts.open",
        unit: "{alert}",
        description: "Reorg Alerts still awaiting operator review.");

    /// <param name="outcome">
    /// <c>completed</c>, <c>skipped</c> when another instance holds the lease,
    /// or <c>failed</c>.
    /// </param>
    public static void RecordWorkerRun(string worker, string outcome) =>
        WorkerRuns.Add(
            1,
            new KeyValuePair<string, object?>("worker.name", worker),
            new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordWebhookAttempt(string result) =>
        WebhookAttempts.Add(1, new KeyValuePair<string, object?>("result", result));

    /// <summary>
    /// One event reached the terminal failed state. The gauge of terminal
    /// events never goes down on its own, because terminal events stay, so an
    /// alert has to ask whether this counter increased instead.
    /// </summary>
    public static void RecordWebhookTerminalTransition() =>
        WebhookTerminalTransitions.Add(1);

    public static void RecordWorkerConsecutiveFailures(string worker, long count) =>
        WorkerConsecutiveFailures.Record(count, new KeyValuePair<string, object?>("worker.name", worker));

    public static void RecordWebhookTerminalFailures(long count) =>
        WebhookTerminalFailures.Record(count);

    public static void RecordObservationUnavailable(string supportedCurrency, bool unavailable) =>
        ObservationUnavailable.Record(
            unavailable ? 1 : 0,
            new KeyValuePair<string, object?>("currency", supportedCurrency));

    public static void RecordAddressPoolAvailable(string supportedCurrency, long count) =>
        AddressPoolAvailable.Record(count, new KeyValuePair<string, object?>("currency", supportedCurrency));

    public static void RecordOpenReorgAlerts(long count) =>
        OpenReorgAlerts.Record(count);
}
