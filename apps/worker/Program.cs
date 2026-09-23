using Payaffe.Application;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Telemetry;
using Payaffe.Infrastructure.Webhooks;
using Payaffe.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// Exit code 78 is `EX_CONFIG`: the host was started with a configuration it
// cannot act on, so the container must fail instead of idling.
const int ConfigurationErrorExitCode = 78;

// The container healthcheck runs this executable again with `health`. It must
// short-circuit before any host, database, or telemetry setup.
if (args.Length > 0 && string.Equals(args[0], WorkerHealthCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await WorkerHealthCommand.RunAsync(CancellationToken.None);
}

var builder = Host.CreateApplicationBuilder(args);
builder.AddPayaffeTelemetry("payaffe-worker");

var connectionString = builder.Configuration.GetConnectionString("Payaffe");
if (string.IsNullOrWhiteSpace(connectionString))
{
    await Console.Error.WriteLineAsync(
        "Worker host configuration is invalid: ConnectionStrings:Payaffe is required.");
    Environment.Exit(ConfigurationErrorExitCode);
}

builder.Services.AddOptions<PaymentApplicationOptions>()
    .Bind(builder.Configuration.GetSection("Payments"))
    .ValidateOnStart();
builder.Services.AddOptions<ExchangeRateOptions>()
    .Bind(builder.Configuration.GetSection("ExchangeRates"))
    .ValidateOnStart();
builder.Services.AddOptions<SimulatedExchangeRateOptions>()
    .Bind(builder.Configuration.GetSection(SimulatedExchangeRateOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<RateCacheRefreshWorkerOptions>()
    .Bind(builder.Configuration.GetSection("ExchangeRates:RefreshWorker"))
    .ValidateOnStart();
builder.Services.AddOptions<PaymentAddressOptions>()
    .Bind(builder.Configuration.GetSection("PaymentAddresses"))
    .ValidateOnStart();
builder.Services.AddOptions<BlockchainObservationOptions>()
    .Bind(builder.Configuration.GetSection("BlockchainObservation"))
    .ValidateOnStart();
builder.Services.AddOptions<BlockchainObservationWorkerOptions>()
    .Bind(builder.Configuration.GetSection("Payments:ObservationWorker"))
    .ValidateOnStart();
builder.Services.AddOptions<ReorgMonitoringWorkerOptions>()
    .Bind(builder.Configuration.GetSection("Payments:ReorgMonitoringWorker"))
    .ValidateOnStart();
builder.Services.AddOptions<PaymentLifecycleWorkerOptions>()
    .Bind(builder.Configuration.GetSection("Payments:LifecycleWorker"))
    .ValidateOnStart();
builder.Services.AddOptions<WebhookDeliveryOptions>()
    .Bind(builder.Configuration.GetSection("Webhooks:Delivery"))
    .ValidateOnStart();
builder.Services.AddOptions<WorkerHealthOptions>()
    .Bind(builder.Configuration.GetSection("Workers:Health"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddPayaffeInstallationMode(builder.Configuration);
builder.Services.AddPayaffeApplication();
builder.Services.AddPayaffeInfrastructure(connectionString!, applySchemaOnStartup: true);

// The heartbeat this host is judged healthy by waits for the schema migration
// to complete (it runs in the background, ADR 0027) and stops while a worker
// loop has died; see WorkerHealthHostedService.
builder.Services.AddHostedService<WorkerHealthHostedService>();

try
{
    await builder.Build().RunAsync();
}
catch (OptionsValidationException exception)
{
    await Console.Error.WriteLineAsync($"Worker host configuration is invalid: {exception.Message}");
    Environment.Exit(ConfigurationErrorExitCode);
}

// The telemetry exporters keep threads alive after the host stops, so the
// process is ended explicitly rather than waiting for them. The code is
// whatever the run set: a host that stopped because its migration failed has
// already put `EX_SOFTWARE` there, and exiting 0 over it would report a broken
// deployment as a clean shutdown.
Environment.Exit(Environment.ExitCode);
return 0;
