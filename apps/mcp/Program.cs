using Payaffe.Application;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Telemetry;
using Payaffe.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Payaffe")
    ?? throw new InvalidOperationException("ConnectionStrings:Payaffe is required.");

// stdio is the MCP transport, so nothing may write to standard output.
builder.AddPayaffeTelemetry("payaffe-mcp", logToStandardError: true);

builder.Services.AddPayaffeApplication();
builder.Services.AddPayaffeInfrastructure(connectionString, registerHostedWorkers: false);
builder.Services.Configure<PaymentAddressOptions>(builder.Configuration.GetSection("PaymentAddresses"));
builder.Services.AddOptions<AdminMcpOptions>()
    .Bind(builder.Configuration.GetSection("Mcp:Admin"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<AdminMcpRateLimiter>();

builder.Services
    .AddMcpServer(options => options.ServerInfo = new() { Name = "payaffe-admin", Version = ProductVersion.Value })
    .WithStdioServerTransport()
    .WithTools<AdminMcpTools>();

try
{
    await builder.Build().RunAsync();
}
catch (OptionsValidationException exception)
{
    // The stdio transport keeps a reader alive, so a failed start would
    // otherwise leave the process hanging instead of reporting a clear exit.
    await Console.Error.WriteLineAsync($"Admin MCP host configuration is invalid: {exception.Message}");
    Environment.Exit(78);
}

// Reached only once the host has stopped; the transport can keep threads alive.
Environment.Exit(0);
