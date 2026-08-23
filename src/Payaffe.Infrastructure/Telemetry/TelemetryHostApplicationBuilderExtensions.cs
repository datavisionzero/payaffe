using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Payaffe.Infrastructure.Telemetry;

public static class TelemetryHostApplicationBuilderExtensions
{
    /// <summary>
    /// Wires the shared observability baseline for a `payaffe` host: structured
    /// JSON logs on the console, plus OTLP export of logs, traces, and metrics
    /// when a collector endpoint is configured.
    /// </summary>
    /// <param name="serviceName">Value for the <c>service.name</c> resource attribute.</param>
    /// <param name="logToStandardError">
    /// Hosts that use stdout as a protocol channel, such as the `stdio` Admin
    /// MCP host, must keep every log line on stderr.
    /// </param>
    /// <param name="configureTracing">
    /// Host-specific instrumentation, such as ASP.NET Core server spans, which
    /// only the API host should register.
    /// </param>
    public static IHostApplicationBuilder AddPayaffeTelemetry(
        this IHostApplicationBuilder builder,
        string serviceName,
        bool logToStandardError = false,
        Action<TracerProviderBuilder>? configureTracing = null,
        Action<MeterProviderBuilder>? configureMetrics = null)
    {
        var section = builder.Configuration.GetSection("Observability");
        builder.Services.Configure<TelemetryOptions>(section);
        builder.Services.AddOptions<OperationalMetricsOptions>()
            .Bind(section.GetSection("OperationalMetrics"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = section.Get<TelemetryOptions>() ?? new TelemetryOptions();
        var otlpEndpoint = ResolveOtlpEndpoint(builder.Configuration, options);
        var resource = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName,
                serviceNamespace: PayaffeTelemetry.ServiceNamespace,
                serviceVersion: ResolveServiceVersion(options),
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(
            [
                new KeyValuePair<string, object>(
                    "deployment.environment",
                    options.DeploymentEnvironment ?? builder.Environment.EnvironmentName),
            ]);

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(console => console.IncludeScopes = true);
        if (logToStandardError)
        {
            builder.Logging.Services.Configure<ConsoleLoggerOptions>(
                console => console.LogToStandardErrorThreshold = LogLevel.Trace);
        }

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.SetResourceBuilder(resource);
            if (otlpEndpoint is not null)
            {
                logging.AddOtlpExporter(exporter => exporter.Endpoint = otlpEndpoint);
            }
        });

        if (!string.IsNullOrWhiteSpace(options.GlitchTipDsn))
        {
            builder.Logging.AddSentry(sentry =>
            {
                sentry.Dsn = options.GlitchTipDsn;
                sentry.Environment = options.DeploymentEnvironment ?? builder.Environment.EnvironmentName;
                sentry.Release = options.ServiceVersion;
                // Error reporting must not become a second channel for payer or
                // Admin data, so request bodies and identities stay out of it.
                sentry.SendDefaultPii = false;
                sentry.MaxBreadcrumbs = 20;
                sentry.MinimumEventLevel = LogLevel.Error;
            });
        }

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resource);
                tracing.AddSource(PayaffeTelemetry.ActivitySourceName);
                tracing.AddHttpClientInstrumentation();
                tracing.AddNpgsql();
                configureTracing?.Invoke(tracing);
                if (otlpEndpoint is not null)
                {
                    tracing.AddOtlpExporter(exporter => exporter.Endpoint = otlpEndpoint);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resource);
                metrics.AddMeter(PayaffeTelemetry.MeterName);
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                metrics.AddNpgsqlInstrumentation();
                configureMetrics?.Invoke(metrics);
                if (otlpEndpoint is not null)
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = otlpEndpoint);
                }
            });

        return builder;
    }

    private static Uri? ResolveOtlpEndpoint(IConfiguration configuration, TelemetryOptions options)
    {
        // `OTEL_EXPORTER_OTLP_ENDPOINT` is the conventional variable operators
        // already set for a collector, so it stays a supported alternative to
        // the product-specific key.
        var configured = string.IsNullOrWhiteSpace(options.OtlpEndpoint)
            ? configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            : options.OtlpEndpoint;

        return Uri.TryCreate(configured, UriKind.Absolute, out var endpoint) ? endpoint : null;
    }

    private static string ResolveServiceVersion(TelemetryOptions options)
    {
        // An installation may report its own build identifier; otherwise the
        // stamped product version is used, so `service.version` and the
        // outbound `User-Agent` always agree.
        return string.IsNullOrWhiteSpace(options.ServiceVersion)
            ? ProductVersion.Value
            : options.ServiceVersion;
    }
}
