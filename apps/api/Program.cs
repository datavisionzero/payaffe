using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Payaffe.Application;
using Payaffe.Application.Admin;
using Payaffe.Application.Payments;
using Payaffe.Domain.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Telemetry;
using Payaffe.Infrastructure.Webhooks;

var builder = WebApplication.CreateBuilder(args);
builder.AddPayaffeTelemetry(
    "payaffe-api",
    configureTracing: tracing => tracing.AddAspNetCoreInstrumentation(),
    configureMetrics: metrics => metrics.AddAspNetCoreInstrumentation());
var webAllowedOrigins = builder.Configuration
    .GetSection("Web:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddProblemDetails();
if (webAllowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy(
            "WebBrowser",
            policy => policy
                .WithOrigins(webAllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials());
    });
}

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-payaffe-admin-csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.Path = "/";
});
builder.Services.AddOpenApi("v1", options =>
{
    options.ShouldInclude = apiDescription => IsIntegrationApiPath(apiDescription.RelativePath);
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "payaffe Integration API",
            Version = "v1",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
        };

        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, _) =>
    {
        if (!IsIntegrationApiPath(context.Description.RelativePath))
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [],
        });

        return Task.CompletedTask;
    });
});
builder.Services.AddOpenApi("web", options =>
{
    options.ShouldInclude = apiDescription => IsWebApiPath(apiDescription.RelativePath);
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "payaffe Web API",
            Version = "web",
        };

        return Task.CompletedTask;
    });
});
builder.Services.Configure<PaymentApplicationOptions>(builder.Configuration.GetSection("Payments"));
builder.Services.AddOptions<ExchangeRateOptions>()
    .Bind(builder.Configuration.GetSection("ExchangeRates"))
    .ValidateOnStart();
builder.Services.AddOptions<RateCacheRefreshWorkerOptions>()
    .Bind(builder.Configuration.GetSection("ExchangeRates:RefreshWorker"))
    .ValidateOnStart();
builder.Services.AddOptions<PaymentAddressOptions>()
    .Bind(builder.Configuration.GetSection("PaymentAddresses"))
    .ValidateOnStart();
builder.Services.Configure<AdminProjectDefaultsOptions>(builder.Configuration.GetSection("PaymentAddresses"));
builder.Services.AddOptions<BlockchainObservationOptions>()
    .Bind(builder.Configuration.GetSection("BlockchainObservation"))
    .ValidateOnStart();
builder.Services.AddOptions<BlockchainObservationWorkerOptions>()
    .Bind(builder.Configuration.GetSection("Payments:ObservationWorker"))
    .ValidateOnStart();
builder.Services.AddOptions<ReorgMonitoringWorkerOptions>()
    .Bind(builder.Configuration.GetSection("Payments:ReorgMonitoringWorker"))
    .ValidateOnStart();
builder.Services.Configure<AdminAuthenticationOptions>(builder.Configuration.GetSection("Admin:Authentication"));
builder.Services.Configure<IntegrationApiRateLimitOptions>(builder.Configuration.GetSection("IntegrationApi:RateLimit"));
builder.Services.Configure<ClientErrorReportOptions>(builder.Configuration.GetSection("Diagnostics:ClientErrors"));
// Every rate limit partitioned by source address, and every audit entry that
// records one, is only as good as the address this host believes in. Behind a
// reverse proxy that address is the proxy's until the proxy is named here
// (ADR 0032). Parsed before the host is built, so a list that is wrong stops
// the start rather than quietly trusting nothing.
var trustedProxies = TrustedProxies.Parse(builder.Configuration[TrustedProxies.ConfigurationKey]);
if (!trustedProxies.IsEmpty)
{
    builder.Services.Configure<ForwardedHeadersOptions>(trustedProxies.ApplyTo);
}
builder.Services.AddOptions<PaymentLifecycleWorkerOptions>()
    .Bind(builder.Configuration.GetSection("Payments:LifecycleWorker"))
    .ValidateOnStart();
builder.Services.AddOptions<WebhookDeliveryOptions>()
    .Bind(builder.Configuration.GetSection("Webhooks:Delivery"))
    .ValidateOnStart();
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = async (context, cancellationToken) =>
    {
        var httpContext = context.HttpContext;
        var integrationApiRequest = IsIntegrationApiPath(httpContext.Request.Path.Value?.TrimStart('/'));
        var problemCode = integrationApiRequest ? "rate_limited" : "admin_rate_limit.exceeded";
        var auditEventType = integrationApiRequest ? "integration_api.rate_limit" : "admin.rate_limit";
        var auditSubjectType = integrationApiRequest ? "integration_api_credential" : "admin_account";
        try
        {
            var clock = httpContext.RequestServices.GetRequiredService<IClock>();
            var adminAuthentication = httpContext.RequestServices.GetRequiredService<AdminAuthenticationService>();
            await adminAuthentication.RecordSecurityAuditAsync(
                new AdminAuditEntry(
                    Guid.NewGuid(),
                    clock.UtcNow,
                    auditEventType,
                    "denied",
                    "system",
                    "unknown",
                    "api",
                    httpContext.Connection.RemoteIpAddress?.ToString(),
                    httpContext.Request.Headers.UserAgent.ToString(),
                    httpContext.TraceIdentifier,
                    problemCode,
                    auditSubjectType,
                    "unknown"),
                cancellationToken);
        }
        catch (Exception exception)
        {
            httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Payaffe.RateLimiting")
                .LogWarning(exception, "Failed to record rate-limit audit entry.");
        }

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests.",
        };
        problemDetails.Extensions["code"] = problemCode;
        problemDetails.Extensions["correlationId"] = httpContext.TraceIdentifier;

        await Results.Json(
            problemDetails,
            statusCode: StatusCodes.Status429TooManyRequests,
            contentType: "application/problem+json")
            .ExecuteAsync(httpContext);
    };
    options.AddPolicy("IntegrationApi", httpContext =>
    {
        var rateLimitOptions = httpContext.RequestServices
            .GetRequiredService<IOptions<IntegrationApiRateLimitOptions>>()
            .Value;

        return RateLimitPartition.GetFixedWindowLimiter(
            GetIntegrationApiRateLimitPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, rateLimitOptions.PermitLimit),
                Window = rateLimitOptions.Window > TimeSpan.Zero
                    ? rateLimitOptions.Window
                    : TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });
    options.AddPolicy("AdminAuthentication", httpContext =>
    {
        var adminOptions = httpContext.RequestServices
            .GetRequiredService<IOptions<AdminAuthenticationOptions>>()
            .Value;
        var partitionKey = string.Join(
            '|',
            httpContext.Request.Path.Value ?? string.Empty,
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, adminOptions.RateLimitPermitLimit),
                Window = adminOptions.RateLimitWindow > TimeSpan.Zero
                    ? adminOptions.RateLimitWindow
                    : TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });
    // The one endpoint anybody on the internet may post to. The limit is per
    // source address and deliberately small: a browser that is failing reports
    // once, and a caller that wants to write a thousand lines into the
    // operator's log store is the case this bounds.
    options.AddPolicy("ClientErrors", httpContext =>
    {
        var clientErrorOptions = httpContext.RequestServices
            .GetRequiredService<IOptions<ClientErrorReportOptions>>()
            .Value;

        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, clientErrorOptions.RateLimitPermitLimit),
                Window = clientErrorOptions.RateLimitWindow > TimeSpan.Zero
                    ? clientErrorOptions.RateLimitWindow
                    : TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });
});
builder.Services.AddPayaffeInstallationMode(builder.Configuration);
builder.Services.AddPayaffeApplication();

// A deployment that runs the dedicated worker host sets this to false so the
// API serves requests only. Worker leases make a temporary overlap safe, but
// running both permanently would double the poll load on every provider.
var runWorkersInApiHost = builder.Configuration.GetValue("Workers:RunInApiHost", true);
builder.Services.AddPayaffeInfrastructure(
    builder.Configuration.GetConnectionString("Payaffe") ?? "Host=localhost;Database=payaffe",
    registerHostedWorkers: runWorkersInApiHost,
    applySchemaOnStartup: true);

var app = builder.Build();

// First in the pipeline, because everything after it -- the rate limiters, the
// audit entries, the scheme a redirect is built from -- reads the address and
// the scheme this replaces.
if (trustedProxies.IsEmpty)
{
    app.Logger.LogInformation(
        "Client addresses come from the connection. {ConfigurationKey} is empty, so X-Forwarded-For is ignored.",
        TrustedProxies.ConfigurationKey);
}
else
{
    app.UseForwardedHeaders();
    app.Logger.LogInformation(
        "Client addresses come from X-Forwarded-For. {TrustedProxyCount} proxy entries are trusted.",
        trustedProxies.Count);
}

if (webAllowedOrigins.Length > 0)
{
    app.UseCors("WebBrowser");
}

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl =
            context.Context.Request.Path.StartsWithSegments("/assets")
                ? "public,max-age=31536000,immutable"
                : "no-cache";
    },
});

app.UseRateLimiter();

app.MapGet("/health/live", () => Results.Ok(new HealthResponse("alive")));
// Readiness is the schema and the database, in that order. Kestrel answers
// before the hosted services have run, so an installation that reported ready
// on connectivity alone would be telling a deployment it is serving while the
// migration this build needs is still being applied (ADR 0027). It still says
// only `ready` or `not_ready`: which migration is pending is not something a
// readiness endpoint tells whoever can reach the port.
app.MapGet("/health/ready", async (
    SchemaMigrationState schemaMigration,
    PayaffeDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    try
    {
        return schemaMigration.IsComplete && await dbContext.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new HealthResponse("ready"))
            : Results.Json(new HealthResponse("not_ready"), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception)
    {
        return Results.Json(new HealthResponse("not_ready"), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});
app.MapOpenApi("/openapi/{documentName}.json");

var integrationApi = app.MapGroup("/api/v1")
    .RequireRateLimiting("IntegrationApi");

integrationApi.MapPost("/payments", CreatePaymentAsync)
    .WithName("CreatePayment")
    .WithTags("Payments")
    .Accepts<CreatePaymentHttpRequest>("application/json")
    .Produces<PaymentResponse>(StatusCodes.Status201Created, "application/json")
    .Produces<PaymentResponse>(StatusCodes.Status200OK, "application/json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json")
    .AddOpenApiOperationTransformer((operation, _, _) =>
    {
        if (operation.RequestBody is OpenApiRequestBody requestBody)
        {
            requestBody.Required = true;
        }

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Required = true,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                MaxLength = 255,
            },
        });

        return Task.CompletedTask;
    });

integrationApi.MapGet("/payments/{paymentId:guid}", GetPaymentAsync)
    .WithName("GetPayment")
    .WithTags("Payments")
    .Produces<PaymentResponse>(StatusCodes.Status200OK, "application/json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json");

integrationApi.MapPut("/payments/{paymentId:guid}/currency-selection", SelectPaymentCurrencyAsync)
    .WithName("SelectPaymentCurrency")
    .WithTags("Payments")
    .Accepts<SelectPaymentCurrencyHttpRequest>("application/json")
    .Produces<PaymentResponse>(StatusCodes.Status200OK, "application/json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json")
    .AddOpenApiOperationTransformer((operation, _, _) =>
    {
        if (operation.RequestBody is OpenApiRequestBody requestBody)
        {
            requestBody.Required = true;
        }

        return Task.CompletedTask;
    });

// Errors the browser could not handle, reported by the payer page and the Admin
// UI so that they reach the same log the backend writes to. An installation
// that does not want a publicly postable endpoint sets
// `Diagnostics:ClientErrors:Enabled` to false and it is not mapped at all.
var clientErrorOptions = builder.Configuration
    .GetSection("Diagnostics:ClientErrors")
    .Get<ClientErrorReportOptions>() ?? new ClientErrorReportOptions();
if (clientErrorOptions.Enabled)
{
    app.MapPost("/api/client-errors", ReportClientErrorAsync)
        .RequireRateLimiting("ClientErrors")
        .ExcludeFromDescription();
}

var payerApi = app.MapGroup("/api/payer");

payerApi.MapGet("/payments/{payerPageId}", GetPayerPaymentAsync)
    .WithName("GetPayerPayment")
    .WithTags("Payer")
    .Produces<PaymentResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json");

payerApi.MapPost("/payments/{payerPageId}/currency-selection", SelectPayerPaymentCurrencyAsync)
    .WithName("SelectPayerPaymentCurrency")
    .WithTags("Payer")
    .Produces<PaymentResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

var adminApi = app.MapGroup("/api/admin");

adminApi.MapGet("/projects", ListAdminProjectsAsync)
    .WithName("ListAdminProjects")
    .WithTags("Admin")
    .Produces<AdminProjectsHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

adminApi.MapPost("/projects", CreateAdminProjectAsync)
    .WithName("CreateAdminProject")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin")
    .Produces<AdminProjectHttpResponse>(StatusCodes.Status201Created)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

adminApi.MapPost("/projects/{projectId:guid}/status", ChangeAdminProjectStatusAsync)
    .WithName("ChangeAdminProjectStatus")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin")
    .Produces<AdminProjectHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

adminApi.MapGet("/csrf", GetAdminCsrfAsync)
    .WithName("GetAdminCsrf")
    .WithTags("Admin")
    .Produces<AdminCsrfHttpResponse>(StatusCodes.Status200OK);

adminApi.MapGet("/session", GetAdminSessionAsync)
    .WithName("GetAdminSession")
    .WithTags("Admin")
    .Produces<AdminSessionHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

adminApi.MapGet("/payments", ListAdminPaymentsAsync)
    .WithName("ListAdminPayments")
    .WithTags("Admin")
    .Produces<AdminPaymentsHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

adminApi.MapGet("/payments/{paymentId:guid}", GetAdminPaymentAsync)
    .WithName("GetAdminPayment")
    .WithTags("Admin")
    .Produces<AdminPaymentDetailReadModel>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json");

adminApi.MapPost("/payments/{paymentId:guid}/settle", SettleAdminPaymentAsync)
    .WithName("SettleAdminPayment")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin")
    .Produces<AdminPaymentDetailReadModel>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

adminApi.MapGet("/reorg-alerts", ListAdminReorgAlertsAsync)
    .WithName("ListAdminReorgAlerts")
    .WithTags("Admin")
    .Produces<AdminReorgAlertsHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

adminApi.MapGet("/observation-health", GetAdminObservationHealthAsync)
    .WithName("GetAdminObservationHealth")
    .WithTags("Admin")
    .Produces<AdminObservationHealthHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

adminApi.MapGet("/audit-log", ListAdminAuditLogAsync)
    .WithName("ListAdminAuditLog")
    .WithTags("Admin")
    .Produces<AdminAuditLogHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

adminApi.MapGet("/audit-log/{eventId:guid}", GetAdminAuditLogEntryAsync)
    .WithName("GetAdminAuditLogEntry")
    .WithTags("Admin")
    .Produces<AdminAuditLogEntryDetailReadModel>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json");

adminApi.MapGet("/webhook-deliveries", ListAdminWebhookDeliveriesAsync)
    .WithName("ListAdminWebhookDeliveries")
    .WithTags("Admin")
    .Produces<AdminWebhookDeliveriesHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

adminApi.MapGet("/integration-api-credentials", ListAdminIntegrationApiCredentialsAsync)
    .WithName("ListAdminIntegrationApiCredentials")
    .WithTags("Admin")
    .Produces<AdminIntegrationApiCredentialsHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

adminApi.MapPost("/integration-api-credentials", CreateAdminIntegrationApiCredentialAsync)
    .WithName("CreateAdminIntegrationApiCredential")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin")
    .Produces<AdminIntegrationApiCredentialSecretHttpResponse>(StatusCodes.Status201Created)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json");

adminApi.MapPost(
        "/integration-api-credentials/{credentialId:guid}/rotate",
        RotateAdminIntegrationApiCredentialAsync)
    .WithName("RotateAdminIntegrationApiCredential")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin")
    .Produces<AdminIntegrationApiCredentialSecretHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

adminApi.MapPost(
        "/integration-api-credentials/{credentialId:guid}/disable",
        DisableAdminIntegrationApiCredentialAsync)
    .WithName("DisableAdminIntegrationApiCredential")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin")
    .Produces<AdminIntegrationApiCredentialHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

adminApi.MapGet("/webhook-endpoints", ListAdminWebhookEndpointsAsync)
    .WithName("ListAdminWebhookEndpoints")
    .WithTags("Admin")
    .Produces<AdminWebhookEndpointsHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

adminApi.MapPost("/webhook-endpoints", CreateAdminWebhookEndpointAsync)
    .WithName("CreateAdminWebhookEndpoint")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin")
    .Produces<AdminWebhookEndpointHttpResponse>(StatusCodes.Status201Created)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

adminApi.MapPost("/webhook-endpoints/{endpointId:guid}/update", UpdateAdminWebhookEndpointAsync)
    .WithName("UpdateAdminWebhookEndpoint")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin")
    .Produces<AdminWebhookEndpointHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

adminApi.MapPost(
        "/webhook-endpoints/{endpointId:guid}/rotate-secret",
        RotateAdminWebhookEndpointSecretAsync)
    .WithName("RotateAdminWebhookEndpointSecret")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin")
    .Produces<AdminWebhookEndpointHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

adminApi.MapPost("/webhook-endpoints/{endpointId:guid}/disable", DisableAdminWebhookEndpointAsync)
    .WithName("DisableAdminWebhookEndpoint")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin")
    .Produces<AdminWebhookEndpointHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

adminApi.MapGet("/native-eth-address-pool", GetAdminNativeEthAddressPoolAsync)
    .WithName("GetAdminNativeEthAddressPool")
    .WithTags("Admin")
    .Produces<AdminNativeEthAddressPoolSummary>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

adminApi.MapPost("/native-eth-address-pool/import", ImportAdminNativeEthAddressPoolAsync)
    .WithName("ImportAdminNativeEthAddressPool")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin")
    .Produces<AdminNativeEthAddressPoolImportHttpResponse>(StatusCodes.Status201Created)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

adminApi.MapPost("/audit-log/export", ExportAdminAuditLogAsync)
    .WithName("ExportAdminAuditLog")
    .WithTags("Admin")
    .Produces<AdminAuditLogExportHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json");

adminApi.MapPost("/webhook-deliveries/{eventId:guid}/resend", ResendAdminWebhookDeliveryAsync)
    .WithName("ResendAdminWebhookDelivery")
    .WithTags("Admin")
    .Produces<AdminWebhookDeliveryResendHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

adminApi.MapPost("/auth/login", StartAdminLoginAsync)
    .WithName("StartAdminLogin")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin Authentication")
    .Produces<AdminLoginStartHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json");

adminApi.MapPost("/auth/mfa", CompleteAdminMfaAsync)
    .WithName("CompleteAdminMfa")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin Authentication")
    .Produces<AdminMfaCompleteHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json");

adminApi.MapPost("/auth/step-up", StepUpAdminAsync)
    .WithName("StepUpAdmin")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin Authentication")
    .Produces<AdminStepUpHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json");

adminApi.MapPost("/auth/recovery-codes", GenerateAdminRecoveryCodesAsync)
    .WithName("GenerateAdminRecoveryCodes")
    .RequireRateLimiting("AdminAuthentication")
    .WithTags("Admin Authentication")
    .Produces<AdminRecoveryCodesHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status429TooManyRequests, "application/problem+json");

adminApi.MapPost("/auth/logout", LogoutAdminAsync)
    .WithName("LogoutAdmin")
    .WithTags("Admin Authentication")
    .Produces<AdminLogoutHttpResponse>(StatusCodes.Status200OK)
    .Produces<IntegrationApiProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

// Only browser document navigations reach the SPA. Backend namespaces,
// non-document requests and missing assets retain a real 404.
app.MapFallback(SpaFallback.ServeIndexAsync);

app.Run();

const string AdminSessionCookieName = "__Host-payaffe-admin";

static bool IsIntegrationApiPath(string? relativePath)
{
    return relativePath is not null &&
        relativePath.StartsWith("api/v1/", StringComparison.Ordinal);
}

static bool IsWebApiPath(string? relativePath)
{
    return relativePath is not null &&
        (relativePath.StartsWith("api/payer/", StringComparison.Ordinal) ||
            relativePath.StartsWith("api/admin/", StringComparison.Ordinal));
}

static string GetIntegrationApiRateLimitPartitionKey(HttpContext httpContext)
{
    var authorizationHeader = httpContext.Request.Headers.Authorization.ToString();
    var sourceIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var path = httpContext.Request.Path.Value ?? string.Empty;
    if (string.IsNullOrWhiteSpace(authorizationHeader))
    {
        return string.Join('|', path, "anonymous", sourceIp);
    }

    var credentialFingerprint = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(authorizationHeader)));
    return string.Join('|', path, credentialFingerprint, sourceIp);
}

static async Task<IResult> CreatePaymentAsync(
    HttpContext httpContext,
    CreatePaymentHttpRequest? request,
    PaymentApplicationService payments,
    IIntegrationApiCredentialAuthenticator authenticator,
    CancellationToken cancellationToken)
{
    var credential = await AuthenticateAsync(httpContext, authenticator, cancellationToken);
    if (credential.Result is not null)
    {
        return credential.Result;
    }

    var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();
    var validationErrors = ValidateCreatePaymentRequest(request, idempotencyKey);
    if (validationErrors.Count > 0)
    {
        return IntegrationApiProblem.Validation(httpContext, validationErrors);
    }

    try
    {
        var result = await payments.CreateAsync(
            credential.Principal!.Id,
            new CreatePaymentCommand(
                request!.FiatCurrency,
                request.FiatAmountMinor,
                request.ExternalReference,
                request.PaymentContext is null
                    ? null
                    : new PaymentContextCommand(
                        request.PaymentContext.Username,
                        request.PaymentContext.CustomerNumber,
                        request.PaymentContext.CartName,
                        request.PaymentContext.Note),
                request.ReturnUrl,
                idempotencyKey),
            cancellationToken);

        return result.Kind switch
        {
            CreatePaymentResultKind.Success when result.CreatedNew =>
                Results.Created($"/api/v1/payments/{result.Payment!.PaymentId}", result.Payment),
            CreatePaymentResultKind.Success =>
                Results.Ok(result.Payment),
            CreatePaymentResultKind.IdempotencyConflict =>
                IntegrationApiProblem.Create(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    "Idempotency conflict.",
                    "idempotency.conflict"),
            CreatePaymentResultKind.ProjectUnavailable =>
                IntegrationApiProblem.Create(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    "Project is not accepting new Payments.",
                    "project.not_active"),
            _ => throw new InvalidOperationException($"Unsupported create result {result.Kind}."),
        };
    }
    catch (DomainRuleException exception)
    {
        return IntegrationApiProblem.Validation(
            httpContext,
            new Dictionary<string, string[]>
            {
                ["request"] = [exception.Code],
            });
    }
}

static async Task<IResult> GetPaymentAsync(
    HttpContext httpContext,
    Guid paymentId,
    PaymentApplicationService payments,
    IIntegrationApiCredentialAuthenticator authenticator,
    CancellationToken cancellationToken)
{
    var credential = await AuthenticateAsync(httpContext, authenticator, cancellationToken);
    if (credential.Result is not null)
    {
        return credential.Result;
    }

    var payment = await payments.FindAsync(credential.Principal!.Id, paymentId, cancellationToken);
    if (payment is null)
    {
        return IntegrationApiProblem.Create(
            httpContext,
            StatusCodes.Status404NotFound,
            "Payment was not found.",
            "payment.not_found");
    }

    return Results.Ok(payment);
}

static async Task<IResult> SelectPaymentCurrencyAsync(
    HttpContext httpContext,
    Guid paymentId,
    SelectPaymentCurrencyHttpRequest? request,
    PaymentApplicationService payments,
    IIntegrationApiCredentialAuthenticator authenticator,
    CancellationToken cancellationToken)
{
    var credential = await AuthenticateAsync(httpContext, authenticator, cancellationToken);
    if (credential.Result is not null)
    {
        return credential.Result;
    }

    if (request is null || string.IsNullOrWhiteSpace(request.SupportedCurrency))
    {
        return IntegrationApiProblem.Validation(
            httpContext,
            new Dictionary<string, string[]>
            {
                ["supportedCurrency"] = ["supported_currency.required"],
            });
    }

    try
    {
        var result = await payments.SelectCurrencyAsync(
            new SelectIntegrationPaymentCurrencyCommand(
                credential.Principal!.Id,
                paymentId,
                request.SupportedCurrency),
            cancellationToken);

        return ToCurrencySelectionResult(httpContext, result);
    }
    catch (DomainRuleException exception)
    {
        return IntegrationApiProblem.Validation(
            httpContext,
            new Dictionary<string, string[]>
            {
                ["request"] = [exception.Code],
            });
    }
}

static async Task<IResult> GetPayerPaymentAsync(
    HttpContext httpContext,
    string payerPageId,
    PaymentApplicationService payments,
    CancellationToken cancellationToken)
{
    try
    {
        var payment = await payments.FindByPayerPageIdAsync(payerPageId, cancellationToken);
        if (payment is null)
        {
            return IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status404NotFound,
                "Payment was not found.",
                "payment.not_found");
        }

        return Results.Ok(payment);
    }
    catch (DomainRuleException exception)
    {
        return IntegrationApiProblem.Validation(
            httpContext,
            new Dictionary<string, string[]>
            {
                ["payerPageId"] = [exception.Code],
            });
    }
}

static async Task<IResult> SelectPayerPaymentCurrencyAsync(
    HttpContext httpContext,
    string payerPageId,
    SelectPayerPaymentCurrencyHttpRequest? request,
    PaymentApplicationService payments,
    CancellationToken cancellationToken)
{
    if (request is null || string.IsNullOrWhiteSpace(request.SupportedCurrency))
    {
        return IntegrationApiProblem.Validation(
            httpContext,
            new Dictionary<string, string[]>
            {
                ["supportedCurrency"] = ["supported_currency.required"],
            });
    }

    try
    {
        var result = await payments.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand(payerPageId, request.SupportedCurrency),
            cancellationToken);

        return ToCurrencySelectionResult(httpContext, result);
    }
    catch (DomainRuleException exception)
    {
        return IntegrationApiProblem.Validation(
            httpContext,
            new Dictionary<string, string[]>
            {
                ["request"] = [exception.Code],
            });
    }
}

static IResult ToCurrencySelectionResult(
    HttpContext httpContext,
    SelectPaymentCurrencyResult result)
{
    return result.Kind switch
    {
        SelectPaymentCurrencyResultKind.Selected or
        SelectPaymentCurrencyResultKind.AlreadySelected =>
            Results.Ok(result.Payment),
        SelectPaymentCurrencyResultKind.CurrencyAlreadySelected =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Payment currency has already been selected.",
                "payment.currency_already_selected"),
        SelectPaymentCurrencyResultKind.NotFound =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status404NotFound,
                "Payment was not found.",
                "payment.not_found"),
        SelectPaymentCurrencyResultKind.UnsupportedCurrency =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Supported Currency is invalid.",
                "supported_currency.unsupported"),
        SelectPaymentCurrencyResultKind.PaymentExpired =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Payment has expired.",
                "payment.expired"),
        SelectPaymentCurrencyResultKind.RateUnavailable =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Exchange rate is unavailable.",
                PaymentOptionUnavailableReasons.ExchangeRate),
        SelectPaymentCurrencyResultKind.PaymentAddressUnavailable =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Payment Address is unavailable.",
                PaymentOptionUnavailableReasons.PaymentAddress),
        SelectPaymentCurrencyResultKind.ObservationUnavailable =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Blockchain Observation is unavailable.",
                PaymentOptionUnavailableReasons.BlockchainObservation),
        SelectPaymentCurrencyResultKind.CurrencyDisabled =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Supported Currency is disabled for the Project.",
                PaymentOptionUnavailableReasons.ProjectConfiguration),
        SelectPaymentCurrencyResultKind.ProjectArchived =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Project is archived.",
                "project.archived"),
        _ => throw new InvalidOperationException($"Unsupported select result {result.Kind}."),
    };
}

static async Task<IResult> StartAdminLoginAsync(
    HttpContext httpContext,
    AdminLoginStartHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    CancellationToken cancellationToken)
{
    var result = await adminAuthentication.StartLoginAsync(
        new AdminLoginStartCommand(
            request?.Username,
            request?.Password,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString(),
            httpContext.TraceIdentifier),
        cancellationToken);

    return result.Kind switch
    {
        AdminLoginStartResultKind.MfaRequired =>
            Results.Ok(new AdminLoginStartHttpResponse("mfa_required", result.ChallengeId)),
        // Signed in on the password alone, because the account has no second
        // factor enrolled (ADR 0028). The cookie is the same one the second
        // step would have set — the caller is authenticated from here.
        AdminLoginStartResultKind.Authenticated =>
            CompleteAdminLoginWithSessionCookie(httpContext, result),
        AdminLoginStartResultKind.InvalidCredentials =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status401Unauthorized,
                "Authentication is invalid.",
                "admin_login.invalid"),
        _ => throw new InvalidOperationException($"Unsupported admin login result {result.Kind}."),
    };
}

static async Task<IResult> GetAdminSessionAsync(
    HttpContext httpContext,
    AdminAuthenticationService adminAuthentication,
    CancellationToken cancellationToken)
{
    var result = await adminAuthentication.AuthenticateSessionAsync(
        httpContext.Request.Cookies[AdminSessionCookieName],
        cancellationToken);

    return result.Kind switch
    {
        AdminSessionAuthenticationResultKind.Authenticated =>
            Results.Ok(new AdminSessionHttpResponse(
                "authenticated",
                result.Principal!.AdminAccountId,
                result.Principal.Username,
                result.Principal.MfaAuthenticatedAt,
                result.Principal.StepUpAuthenticatedAt,
                result.Principal.ExpiresAt,
                result.Principal.IdleExpiresAt)),
        AdminSessionAuthenticationResultKind.Invalid =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status401Unauthorized,
                "Authentication is invalid.",
                "admin_session.invalid"),
        _ => throw new InvalidOperationException($"Unsupported admin session result {result.Kind}."),
    };
}

static async Task<IResult> ListAdminProjectsAsync(
    HttpContext httpContext,
    AdminAuthenticationService adminAuthentication,
    AdminProjectService projects,
    CancellationToken cancellationToken)
{
    var admin = await AuthenticateAdminSessionAsync(httpContext, adminAuthentication, cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    return Results.Ok(new AdminProjectsHttpResponse(await projects.ListAsync(cancellationToken)));
}

static async Task<IResult> CreateAdminProjectAsync(
    HttpContext httpContext,
    AdminProjectCreateHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    AdminProjectService projects,
    IAntiforgery antiforgery,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    CancellationToken cancellationToken)
{
    var admin = await AuthorizeSensitiveAdminMutationAsync(
        httpContext, adminAuthentication, antiforgery, clock, adminOptions.Value,
        "admin.project.create", "project", "new", cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    var result = await projects.CreateAsync(
        request?.Name,
        request?.Slug,
        CreateAdminOperationContext(httpContext, admin.Principal!),
        cancellationToken);
    return result.Kind switch
    {
        AdminProjectResultKind.Updated => Results.Created(
            $"/api/admin/projects/{result.Project!.ProjectId:D}",
            new AdminProjectHttpResponse(result.Project)),
        AdminProjectResultKind.InvalidInput => IntegrationApiProblem.Validation(
            httpContext,
            new Dictionary<string, string[]> { ["project"] = ["project.invalid"] }),
        AdminProjectResultKind.SlugConflict => IntegrationApiProblem.Create(
            httpContext, StatusCodes.Status409Conflict, "Project slug already exists.", "project.slug_conflict"),
        _ => throw new InvalidOperationException($"Unsupported project create result {result.Kind}."),
    };
}

static async Task<IResult> ChangeAdminProjectStatusAsync(
    HttpContext httpContext,
    Guid projectId,
    AdminProjectStatusHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    AdminProjectService projects,
    IAntiforgery antiforgery,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    CancellationToken cancellationToken)
{
    var admin = await AuthorizeSensitiveAdminMutationAsync(
        httpContext, adminAuthentication, antiforgery, clock, adminOptions.Value,
        "admin.project.change_status", "project", projectId.ToString("D"), cancellationToken,
        projectId);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    var result = await projects.ChangeStatusAsync(
        projectId,
        request?.ExpectedVersion ?? 0,
        request?.Status,
        CreateAdminOperationContext(httpContext, admin.Principal!),
        cancellationToken);
    return result.Kind switch
    {
        AdminProjectResultKind.Updated => Results.Ok(new AdminProjectHttpResponse(result.Project!)),
        AdminProjectResultKind.InvalidInput => IntegrationApiProblem.Validation(
            httpContext,
            new Dictionary<string, string[]> { ["project"] = ["project.invalid"] }),
        AdminProjectResultKind.NotFound => IntegrationApiProblem.Create(
            httpContext, StatusCodes.Status404NotFound, "Project was not found.", "project.not_found"),
        AdminProjectResultKind.InvalidTransition => IntegrationApiProblem.Create(
            httpContext, StatusCodes.Status409Conflict, "Project status transition is invalid.", "project.status_transition_invalid"),
        AdminProjectResultKind.HasActiveWork => IntegrationApiProblem.Create(
            httpContext, StatusCodes.Status409Conflict, "Project still has active work.", "project.has_active_work"),
        AdminProjectResultKind.ConcurrencyConflict => IntegrationApiProblem.Create(
            httpContext, StatusCodes.Status409Conflict, "Project changed concurrently.", "project.concurrency_conflict",
            new Dictionary<string, object?> { ["currentVersion"] = result.Project!.Version }),
        _ => throw new InvalidOperationException($"Unsupported project status result {result.Kind}."),
    };
}

static async Task<IResult> ListAdminIntegrationApiCredentialsAsync(
    HttpContext httpContext,
    Guid? projectId,
    AdminAuthenticationService adminAuthentication,
    AdminIntegrationApiCredentialService credentials,
    CancellationToken cancellationToken)
{
    var admin = await AuthenticateAdminSessionAsync(httpContext, adminAuthentication, cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, projectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var result = await credentials.ListAsync(projectId ?? Guid.Empty, cancellationToken);
    return Results.Ok(new AdminIntegrationApiCredentialsHttpResponse(result));
}

static async Task<IResult> CreateAdminIntegrationApiCredentialAsync(
    HttpContext httpContext,
    AdminIntegrationApiCredentialCreateHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    AdminIntegrationApiCredentialService credentials,
    IAntiforgery antiforgery,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    CancellationToken cancellationToken)
{
    var admin = await AuthorizeSensitiveAdminMutationAsync(
        httpContext,
        adminAuthentication,
        antiforgery,
        clock,
        adminOptions.Value,
        "admin.integration_api_credential.create",
        "integration_api_credential",
        "new",
        cancellationToken,
        request?.ProjectId);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, request?.ProjectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var result = await credentials.CreateAsync(
        request?.ProjectId ?? Guid.Empty,
        request?.Name,
        CreateAdminOperationContext(httpContext, admin.Principal!),
        cancellationToken);
    return result.Kind switch
    {
        AdminIntegrationApiCredentialCreateResultKind.Created =>
            Results.Created(
                $"/api/admin/integration-api-credentials/{result.Credential!.Id:D}",
                new AdminIntegrationApiCredentialSecretHttpResponse(
                    result.Credential,
                    result.Token!)),
        AdminIntegrationApiCredentialCreateResultKind.InvalidName =>
            IntegrationApiProblem.Validation(
                httpContext,
                new Dictionary<string, string[]>
                {
                    ["name"] = ["integration_api_credential.name.invalid"],
                }),
        AdminIntegrationApiCredentialCreateResultKind.ProjectUnavailable =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Project is not accepting new integration configuration.",
                "project.not_active"),
        _ => throw new InvalidOperationException($"Unsupported credential create result {result.Kind}."),
    };
}

static async Task<IResult> RotateAdminIntegrationApiCredentialAsync(
    HttpContext httpContext,
    Guid credentialId,
    AdminIntegrationApiCredentialMutationHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    AdminIntegrationApiCredentialService credentials,
    IAntiforgery antiforgery,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    CancellationToken cancellationToken)
{
    var admin = await AuthorizeSensitiveAdminMutationAsync(
        httpContext,
        adminAuthentication,
        antiforgery,
        clock,
        adminOptions.Value,
        "admin.integration_api_credential.rotate",
        "integration_api_credential",
        credentialId.ToString("D"),
        cancellationToken,
        request?.ProjectId);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, request?.ProjectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var result = await credentials.RotateAsync(
        request?.ProjectId ?? Guid.Empty,
        credentialId,
        request?.ExpectedVersion ?? 0,
        CreateAdminOperationContext(httpContext, admin.Principal!),
        cancellationToken);
    return MapCredentialMutationResult(httpContext, result, includeToken: true);
}

static async Task<IResult> DisableAdminIntegrationApiCredentialAsync(
    HttpContext httpContext,
    Guid credentialId,
    AdminIntegrationApiCredentialMutationHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    AdminIntegrationApiCredentialService credentials,
    IAntiforgery antiforgery,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    CancellationToken cancellationToken)
{
    var admin = await AuthorizeSensitiveAdminMutationAsync(
        httpContext,
        adminAuthentication,
        antiforgery,
        clock,
        adminOptions.Value,
        "admin.integration_api_credential.disable",
        "integration_api_credential",
        credentialId.ToString("D"),
        cancellationToken,
        request?.ProjectId);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, request?.ProjectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var result = await credentials.DisableAsync(
        request?.ProjectId ?? Guid.Empty,
        credentialId,
        request?.ExpectedVersion ?? 0,
        CreateAdminOperationContext(httpContext, admin.Principal!),
        cancellationToken);
    return MapCredentialMutationResult(httpContext, result, includeToken: false);
}

static async Task<IResult> ListAdminWebhookEndpointsAsync(
    HttpContext httpContext,
    Guid? projectId,
    Guid? integrationApiCredentialId,
    AdminAuthenticationService adminAuthentication,
    AdminWebhookEndpointService endpoints,
    CancellationToken cancellationToken)
{
    var admin = await AuthenticateAdminSessionAsync(httpContext, adminAuthentication, cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, projectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var result = await endpoints.ListAsync(projectId ?? Guid.Empty, integrationApiCredentialId, cancellationToken);
    return Results.Ok(new AdminWebhookEndpointsHttpResponse(result));
}

static async Task<IResult> CreateAdminWebhookEndpointAsync(
    HttpContext httpContext,
    AdminWebhookEndpointCreateHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    AdminWebhookEndpointService endpoints,
    IAntiforgery antiforgery,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    CancellationToken cancellationToken)
{
    var admin = await AuthorizeSensitiveAdminMutationAsync(
        httpContext,
        adminAuthentication,
        antiforgery,
        clock,
        adminOptions.Value,
        "admin.webhook_endpoint.create",
        "webhook_endpoint",
        "new",
        cancellationToken,
        request?.ProjectId);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, request?.ProjectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var result = await endpoints.CreateAsync(
        request?.ProjectId ?? Guid.Empty,
        request?.IntegrationApiCredentialId ?? Guid.Empty,
        request?.Url,
        request?.SecretReference,
        request?.EventTypes,
        CreateAdminOperationContext(httpContext, admin.Principal!),
        cancellationToken);
    return result.Kind == AdminWebhookEndpointResultKind.Success
        ? Results.Created(
            $"/api/admin/webhook-endpoints/{result.Endpoint!.Id:D}",
            new AdminWebhookEndpointHttpResponse(result.Endpoint))
        : MapWebhookEndpointFailure(httpContext, result);
}

static async Task<IResult> UpdateAdminWebhookEndpointAsync(
    HttpContext httpContext,
    Guid endpointId,
    AdminWebhookEndpointUpdateHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    AdminWebhookEndpointService endpoints,
    IAntiforgery antiforgery,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    CancellationToken cancellationToken)
{
    var admin = await AuthorizeSensitiveAdminMutationAsync(
        httpContext,
        adminAuthentication,
        antiforgery,
        clock,
        adminOptions.Value,
        "admin.webhook_endpoint.update",
        "webhook_endpoint",
        endpointId.ToString("D"),
        cancellationToken,
        request?.ProjectId);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, request?.ProjectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var result = await endpoints.UpdateAsync(
        request?.ProjectId ?? Guid.Empty,
        endpointId,
        request?.ExpectedVersion ?? 0,
        request?.Url,
        request?.EventTypes,
        CreateAdminOperationContext(httpContext, admin.Principal!),
        cancellationToken);
    return result.Kind == AdminWebhookEndpointResultKind.Success
        ? Results.Ok(new AdminWebhookEndpointHttpResponse(result.Endpoint!))
        : MapWebhookEndpointFailure(httpContext, result);
}

static async Task<IResult> RotateAdminWebhookEndpointSecretAsync(
    HttpContext httpContext,
    Guid endpointId,
    AdminWebhookEndpointSecretRotationHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    AdminWebhookEndpointService endpoints,
    IAntiforgery antiforgery,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    CancellationToken cancellationToken)
{
    var admin = await AuthorizeSensitiveAdminMutationAsync(
        httpContext,
        adminAuthentication,
        antiforgery,
        clock,
        adminOptions.Value,
        "admin.webhook_endpoint.rotate_secret",
        "webhook_endpoint",
        endpointId.ToString("D"),
        cancellationToken,
        request?.ProjectId);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, request?.ProjectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var result = await endpoints.RotateSecretAsync(
        request?.ProjectId ?? Guid.Empty,
        endpointId,
        request?.ExpectedVersion ?? 0,
        request?.SecretReference,
        CreateAdminOperationContext(httpContext, admin.Principal!),
        cancellationToken);
    return result.Kind == AdminWebhookEndpointResultKind.Success
        ? Results.Ok(new AdminWebhookEndpointHttpResponse(result.Endpoint!))
        : MapWebhookEndpointFailure(httpContext, result);
}

static async Task<IResult> DisableAdminWebhookEndpointAsync(
    HttpContext httpContext,
    Guid endpointId,
    AdminIntegrationApiCredentialMutationHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    AdminWebhookEndpointService endpoints,
    IAntiforgery antiforgery,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    CancellationToken cancellationToken)
{
    var admin = await AuthorizeSensitiveAdminMutationAsync(
        httpContext,
        adminAuthentication,
        antiforgery,
        clock,
        adminOptions.Value,
        "admin.webhook_endpoint.disable",
        "webhook_endpoint",
        endpointId.ToString("D"),
        cancellationToken,
        request?.ProjectId);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, request?.ProjectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var result = await endpoints.DisableAsync(
        request?.ProjectId ?? Guid.Empty,
        endpointId,
        request?.ExpectedVersion ?? 0,
        CreateAdminOperationContext(httpContext, admin.Principal!),
        cancellationToken);
    return result.Kind == AdminWebhookEndpointResultKind.Success
        ? Results.Ok(new AdminWebhookEndpointHttpResponse(result.Endpoint!))
        : MapWebhookEndpointFailure(httpContext, result);
}

static async Task<IResult> GetAdminNativeEthAddressPoolAsync(
    HttpContext httpContext,
    Guid? projectId,
    AdminAuthenticationService adminAuthentication,
    AdminNativeEthAddressPoolService addressPool,
    IOptions<PaymentAddressOptions> addressOptions,
    CancellationToken cancellationToken)
{
    var admin = await AuthenticateAdminSessionAsync(
        httpContext,
        adminAuthentication,
        cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, projectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var summary = await addressPool.GetSummaryAsync(
        projectId ?? Guid.Empty,
        addressOptions.Value.NativeEthLowCapacityThreshold,
        cancellationToken);
    return Results.Ok(summary);
}

static async Task<IResult> ImportAdminNativeEthAddressPoolAsync(
    HttpContext httpContext,
    AdminNativeEthAddressPoolImportHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    AdminNativeEthAddressPoolService addressPool,
    IAntiforgery antiforgery,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    IOptions<PaymentAddressOptions> addressOptions,
    CancellationToken cancellationToken)
{
    var admin = await AuthorizeSensitiveAdminMutationAsync(
        httpContext,
        adminAuthentication,
        antiforgery,
        clock,
        adminOptions.Value,
        "admin.native_eth_address_pool.import",
        "address_pool_import",
        "new",
        cancellationToken,
        request?.ProjectId);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, request?.ProjectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var result = await addressPool.ImportAsync(
        request?.ProjectId ?? Guid.Empty,
        request?.Addresses,
        addressOptions.Value.NativeEthLowCapacityThreshold,
        CreateAdminOperationContext(httpContext, admin.Principal!),
        cancellationToken);
    return result.Kind switch
    {
        AdminNativeEthAddressPoolImportResultKind.Success => Results.Created(
            $"/api/admin/native-eth-address-pool/imports/{result.ImportId:D}",
            new AdminNativeEthAddressPoolImportHttpResponse(
                result.ImportId!.Value,
                result.ImportedCount,
                result.Summary!)),
        AdminNativeEthAddressPoolImportResultKind.InvalidInput =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Native ETH Address Pool import is invalid.",
                "native_eth_address_pool.invalid"),
        AdminNativeEthAddressPoolImportResultKind.DuplicateAddress =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Native ETH Address Pool import contains an existing address.",
                "native_eth_address_pool.duplicate"),
        AdminNativeEthAddressPoolImportResultKind.ProjectUnavailable =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Project is not accepting Address Pool configuration.",
                "project.not_active"),
        _ => throw new InvalidOperationException(
            $"Unsupported native ETH Address Pool import result {result.Kind}."),
    };
}

static async Task<IResult> ListAdminPaymentsAsync(
    HttpContext httpContext,
    Guid? projectId,
    int? limit,
    AdminAuthenticationService adminAuthentication,
    AdminPaymentQueryService adminPayments,
    CancellationToken cancellationToken)
{
    var admin = await AuthenticateAdminSessionAsync(httpContext, adminAuthentication, cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, projectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var payments = await adminPayments.ListRecentPaymentsAsync(projectId ?? Guid.Empty, limit, cancellationToken);
    return Results.Ok(new AdminPaymentsHttpResponse(payments));
}

static async Task<IResult> GetAdminPaymentAsync(
    HttpContext httpContext,
    Guid? projectId,
    Guid paymentId,
    AdminAuthenticationService adminAuthentication,
    AdminPaymentQueryService adminPayments,
    CancellationToken cancellationToken)
{
    var admin = await AuthenticateAdminSessionAsync(httpContext, adminAuthentication, cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, projectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var payment = await adminPayments.FindPaymentAsync(projectId ?? Guid.Empty, paymentId, cancellationToken);
    return payment is null
        ? IntegrationApiProblem.Create(
            httpContext,
            StatusCodes.Status404NotFound,
            "Payment was not found.",
            "payment.not_found")
        : Results.Ok(payment);
}

static async Task<IResult> SettleAdminPaymentAsync(
    HttpContext httpContext,
    Guid paymentId,
    AdminPaymentSettlementHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    AdminPaymentQueryService adminPayments,
    IAntiforgery antiforgery,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    CancellationToken cancellationToken)
{
    var admin = await AuthorizeSensitiveAdminMutationAsync(
        httpContext,
        adminAuthentication,
        antiforgery,
        clock,
        adminOptions.Value,
        "admin.payment.settle",
        "payment",
        paymentId.ToString("D"),
        cancellationToken,
        request?.ProjectId);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, request?.ProjectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var result = await adminPayments.SettleAsync(
        request?.ProjectId ?? Guid.Empty,
        paymentId,
        request?.ExpectedVersion ?? 0,
        request?.Reason,
        CreateAdminOperationContext(httpContext, admin.Principal!),
        cancellationToken);
    if (result.Kind != AdminPaymentSettlementResultKind.Settled)
    {
        var denialCode = result.Kind switch
        {
            AdminPaymentSettlementResultKind.NotFound => "payment.not_found",
            AdminPaymentSettlementResultKind.NotSettleable => "payment.not_settleable",
            AdminPaymentSettlementResultKind.ConcurrencyConflict => "concurrency.conflict",
            AdminPaymentSettlementResultKind.InvalidInput => "payment_settlement.invalid",
            _ => "unexpected_error",
        };
        await adminAuthentication.RecordSecurityAuditAsync(
            CreateAdminAuditEntry(
                httpContext,
                admin.Principal!,
                clock.UtcNow,
                "admin.payment.settle",
                "denied",
                denialCode,
                "payment",
                paymentId.ToString("D"),
                request!.ProjectId),
            cancellationToken);
    }
    return result.Kind switch
    {
        AdminPaymentSettlementResultKind.Settled => Results.Ok(result.Payment),
        AdminPaymentSettlementResultKind.NotFound => IntegrationApiProblem.Create(
            httpContext,
            StatusCodes.Status404NotFound,
            "Payment was not found.",
            "payment.not_found"),
        AdminPaymentSettlementResultKind.NotSettleable => IntegrationApiProblem.Create(
            httpContext,
            StatusCodes.Status409Conflict,
            "Payment is not eligible for manual Settlement.",
            "payment.not_settleable"),
        AdminPaymentSettlementResultKind.ConcurrencyConflict => IntegrationApiProblem.Create(
            httpContext,
            StatusCodes.Status409Conflict,
            "Payment changed before the operation completed.",
            "concurrency.conflict"),
        AdminPaymentSettlementResultKind.InvalidInput => IntegrationApiProblem.Create(
            httpContext,
            StatusCodes.Status400BadRequest,
            "Manual Settlement input is invalid.",
            "payment_settlement.invalid"),
        _ => throw new InvalidOperationException(
            $"Unsupported payment settlement result {result.Kind}."),
    };
}

static async Task<IResult> ListAdminReorgAlertsAsync(
    HttpContext httpContext,
    Guid? projectId,
    int? limit,
    AdminAuthenticationService adminAuthentication,
    AdminPaymentQueryService adminPayments,
    CancellationToken cancellationToken)
{
    var admin = await AuthenticateAdminSessionAsync(
        httpContext,
        adminAuthentication,
        cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, projectId) is { } projectProblem)
    {
        return projectProblem;
    }

    return Results.Ok(new AdminReorgAlertsHttpResponse(
        await adminPayments.ListReorgAlertsAsync(projectId ?? Guid.Empty, limit, cancellationToken)));
}

static async Task<IResult> GetAdminObservationHealthAsync(
    HttpContext httpContext,
    AdminAuthenticationService adminAuthentication,
    IObservationHealthStore healthStore,
    IOptions<BlockchainObservationOptions> observationOptions,
    CancellationToken cancellationToken)
{
    var admin = await AuthenticateAdminSessionAsync(
        httpContext,
        adminAuthentication,
        cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    var persisted = await healthStore.ListAsync(cancellationToken);
    var byCurrency = persisted.ToDictionary(
        health => health.SupportedCurrency,
        StringComparer.Ordinal);
    var providerName = BlockchainObservationOptions.NormalizeMode(
        observationOptions.Value.Mode);
    var configuredAvailable = providerName is "blockchair" or "nownodes";
    var health = new[] { "BTC", "LTC", "ETH" }
        .Select(currency => byCurrency.GetValueOrDefault(currency) ??
            new ObservationHealthReadModel(
                currency,
                providerName,
                configuredAvailable ? "available" : "unavailable",
                LastSuccessfulAt: null,
                LastFailedAt: null,
                configuredAvailable ? null : "observation_provider.not_configured"))
        .ToArray();
    return Results.Ok(new AdminObservationHealthHttpResponse(health));
}

static async Task<IResult> ListAdminAuditLogAsync(
    HttpContext httpContext,
    Guid? projectId,
    int? limit,
    AdminAuthenticationService adminAuthentication,
    AdminAuditLogQueryService auditLog,
    IClock clock,
    CancellationToken cancellationToken)
{
    var admin = await AuthenticateAdminSessionAsync(httpContext, adminAuthentication, cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    var accessAuditEntry = CreateAdminAuditEntry(
        httpContext,
        admin.Principal!,
        clock.UtcNow,
        "admin.audit_log.list",
        "success",
        "audit_log.listed",
        "audit_log",
        "recent",
        projectId);
    var entries = await auditLog.ListRecentEntriesAndRecordAccessAsync(
        projectId,
        limit,
        accessAuditEntry,
        cancellationToken);

    return Results.Ok(new AdminAuditLogHttpResponse(entries));
}

static async Task<IResult> GetAdminAuditLogEntryAsync(
    HttpContext httpContext,
    Guid? projectId,
    Guid eventId,
    AdminAuthenticationService adminAuthentication,
    AdminAuditLogQueryService auditLog,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    CancellationToken cancellationToken)
{
    var admin = await AuthenticateAdminSessionAsync(httpContext, adminAuthentication, cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    var occurredAt = clock.UtcNow;
    if (!HasRecentStepUp(admin.Principal!, occurredAt, adminOptions.Value))
    {
        await auditLog.RecordAccessAsync(
            CreateAdminAuditEntry(
                httpContext,
                admin.Principal!,
                occurredAt,
                "admin.audit_log.detail",
                "denied",
                "admin_step_up.required",
                "audit_log",
                eventId.ToString("D"),
                projectId),
            cancellationToken);

        return IntegrationApiProblem.Create(
            httpContext,
            StatusCodes.Status403Forbidden,
            "Step-up authentication is required.",
            "admin_step_up.required");
    }

    var detail = await auditLog.FindEntryAndRecordAccessAsync(
        projectId,
        eventId,
        CreateAdminAuditEntry(
            httpContext,
            admin.Principal!,
            occurredAt,
            "admin.audit_log.detail",
            "success",
            "audit_log.detail_accessed",
            "audit_log",
            eventId.ToString("D"),
            projectId),
        cancellationToken);
    if (detail is null)
    {
        await auditLog.RecordAccessAsync(
            CreateAdminAuditEntry(
                httpContext,
                admin.Principal!,
                occurredAt,
                "admin.audit_log.detail",
                "failure",
                "audit_log.not_found",
                "audit_log",
                eventId.ToString("D"),
                projectId),
            cancellationToken);

        return IntegrationApiProblem.Create(
            httpContext,
            StatusCodes.Status404NotFound,
            "Audit Log entry was not found.",
            "audit_log.not_found");
    }

    return Results.Ok(detail);
}

static async Task<IResult> ExportAdminAuditLogAsync(
    HttpContext httpContext,
    AdminAuditLogExportHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    AdminAuditLogQueryService auditLog,
    IAntiforgery antiforgery,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    CancellationToken cancellationToken)
{
    if (httpContext.Request.Cookies.ContainsKey(AdminSessionCookieName))
    {
        var csrfProblem = await ValidateAdminCsrfAsync(httpContext, antiforgery);
        if (csrfProblem is not null)
        {
            return csrfProblem;
        }
    }

    var admin = await AuthenticateAdminSessionAsync(httpContext, adminAuthentication, cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    var exportedAt = clock.UtcNow;
    if (!HasRecentStepUp(admin.Principal!, exportedAt, adminOptions.Value))
    {
        await auditLog.RecordAccessAsync(
            CreateAdminAuditEntry(
                httpContext,
                admin.Principal!,
                exportedAt,
                "admin.audit_log.export",
                "denied",
                "admin_step_up.required",
                "audit_log",
                "recent",
                request?.ProjectId),
            cancellationToken);

        return IntegrationApiProblem.Create(
            httpContext,
            StatusCodes.Status403Forbidden,
            "Step-up authentication is required.",
            "admin_step_up.required");
    }

    var entries = await auditLog.ExportRecentEntriesAndRecordAccessAsync(
        request?.ProjectId,
        request?.Limit,
        CreateAdminAuditEntry(
            httpContext,
            admin.Principal!,
            exportedAt,
            "admin.audit_log.export",
            "success",
            "audit_log.exported",
            "audit_log",
            "recent",
            request?.ProjectId),
        cancellationToken);

    return Results.Ok(new AdminAuditLogExportHttpResponse(exportedAt, entries));
}

static async Task<IResult> ListAdminWebhookDeliveriesAsync(
    HttpContext httpContext,
    Guid? projectId,
    int? limit,
    AdminAuthenticationService adminAuthentication,
    AdminWebhookDeliveryQueryService webhookDeliveries,
    CancellationToken cancellationToken)
{
    var admin = await AuthenticateAdminSessionAsync(httpContext, adminAuthentication, cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, projectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var deliveries = await webhookDeliveries.ListResendableDeliveriesAsync(projectId ?? Guid.Empty, limit, cancellationToken);
    return Results.Ok(new AdminWebhookDeliveriesHttpResponse(deliveries));
}

static IResult GetAdminCsrfAsync(
    HttpContext httpContext,
    IAntiforgery antiforgery)
{
    var tokens = antiforgery.GetAndStoreTokens(httpContext);
    if (string.IsNullOrWhiteSpace(tokens.RequestToken))
    {
        throw new InvalidOperationException("ASP.NET Core antiforgery did not issue a request token.");
    }

    httpContext.Response.Headers.CacheControl = "no-store";
    httpContext.Response.Headers.Pragma = "no-cache";
    return Results.Ok(new AdminCsrfHttpResponse(tokens.RequestToken));
}

static async Task<IResult> CompleteAdminMfaAsync(
    HttpContext httpContext,
    AdminMfaCompleteHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    CancellationToken cancellationToken)
{
    var result = await adminAuthentication.CompleteMfaAsync(
        new AdminMfaCompleteCommand(
            request?.ChallengeId,
            request?.TotpCode,
            request?.RecoveryCode,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString(),
            httpContext.TraceIdentifier),
        cancellationToken);

    return result.Kind switch
    {
        AdminMfaCompleteResultKind.Authenticated =>
            CompleteAdminMfaWithSessionCookie(httpContext, result),
        AdminMfaCompleteResultKind.Invalid =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status401Unauthorized,
                "Authentication is invalid.",
                "admin_mfa.invalid"),
        _ => throw new InvalidOperationException($"Unsupported admin MFA result {result.Kind}."),
    };
}

static IResult CompleteAdminLoginWithSessionCookie(
    HttpContext httpContext,
    AdminLoginStartResult result)
{
    AppendAdminSessionCookie(httpContext, result.SessionToken!, result.SessionExpiresAt!.Value);
    return Results.Ok(new AdminLoginStartHttpResponse("authenticated", ChallengeId: null));
}

static void AppendAdminSessionCookie(
    HttpContext httpContext,
    string sessionToken,
    DateTimeOffset expiresAt)
{
    httpContext.Response.Cookies.Append(
        AdminSessionCookieName,
        sessionToken,
        new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expiresAt,
        });
}

static IResult CompleteAdminMfaWithSessionCookie(
    HttpContext httpContext,
    AdminMfaCompleteResult result)
{
    AppendAdminSessionCookie(httpContext, result.SessionToken!, result.ExpiresAt!.Value);
    return Results.Ok(new AdminMfaCompleteHttpResponse("authenticated"));
}

static async Task<IResult> StepUpAdminAsync(
    HttpContext httpContext,
    AdminStepUpHttpRequest? request,
    AdminAuthenticationService adminAuthentication,
    IAntiforgery antiforgery,
    CancellationToken cancellationToken)
{
    if (httpContext.Request.Cookies.ContainsKey(AdminSessionCookieName))
    {
        var csrfProblem = await ValidateAdminCsrfAsync(httpContext, antiforgery);
        if (csrfProblem is not null)
        {
            return csrfProblem;
        }
    }

    var result = await adminAuthentication.StepUpAsync(
        new AdminStepUpCommand(
            httpContext.Request.Cookies[AdminSessionCookieName],
            request?.TotpCode,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString(),
            httpContext.TraceIdentifier),
        cancellationToken);

    return result.Kind switch
    {
        AdminStepUpResultKind.Authenticated =>
            Results.Ok(new AdminStepUpHttpResponse(
                "step_up_authenticated",
                result.StepUpAuthenticatedAt!.Value,
                result.IdleExpiresAt!.Value)),
        AdminStepUpResultKind.Invalid =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status401Unauthorized,
                "Authentication is invalid.",
                "admin_step_up.invalid"),
        _ => throw new InvalidOperationException($"Unsupported admin step-up result {result.Kind}."),
    };
}

static async Task<IResult> GenerateAdminRecoveryCodesAsync(
    HttpContext httpContext,
    AdminAuthenticationService adminAuthentication,
    IAntiforgery antiforgery,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    CancellationToken cancellationToken)
{
    if (httpContext.Request.Cookies.ContainsKey(AdminSessionCookieName))
    {
        var csrfProblem = await ValidateAdminCsrfAsync(httpContext, antiforgery);
        if (csrfProblem is not null)
        {
            return csrfProblem;
        }
    }

    var admin = await AuthenticateAdminSessionAsync(httpContext, adminAuthentication, cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    var occurredAt = clock.UtcNow;
    if (!HasRecentStepUp(admin.Principal!, occurredAt, adminOptions.Value))
    {
        await adminAuthentication.RecordSecurityAuditAsync(
            CreateAdminAuditEntry(
                httpContext,
                admin.Principal!,
                occurredAt,
                "admin.recovery_codes.generate",
                "denied",
                "admin_step_up.required",
                "admin_account",
                admin.Principal!.AdminAccountId.ToString("D")),
            cancellationToken);

        return IntegrationApiProblem.Create(
            httpContext,
            StatusCodes.Status403Forbidden,
            "Step-up authentication is required.",
            "admin_step_up.required");
    }

    var result = await adminAuthentication.GenerateRecoveryCodesAsync(
        admin.Principal!,
        httpContext.Connection.RemoteIpAddress?.ToString(),
        httpContext.Request.Headers.UserAgent.ToString(),
        httpContext.TraceIdentifier,
        cancellationToken);

    return Results.Ok(new AdminRecoveryCodesHttpResponse(
        result.GeneratedAt,
        result.RecoveryCodes));
}

static async Task<IResult> ResendAdminWebhookDeliveryAsync(
    HttpContext httpContext,
    Guid? projectId,
    Guid eventId,
    AdminAuthenticationService adminAuthentication,
    WebhookDeliveryProcessor webhookDelivery,
    IAntiforgery antiforgery,
    IClock clock,
    IOptions<AdminAuthenticationOptions> adminOptions,
    CancellationToken cancellationToken)
{
    if (httpContext.Request.Cookies.ContainsKey(AdminSessionCookieName))
    {
        var csrfProblem = await ValidateAdminCsrfAsync(httpContext, antiforgery);
        if (csrfProblem is not null)
        {
            return csrfProblem;
        }
    }

    var admin = await AuthenticateAdminSessionAsync(httpContext, adminAuthentication, cancellationToken);
    if (admin.Result is not null)
    {
        return admin.Result;
    }

    if (ValidateProjectContext(httpContext, projectId) is { } projectProblem)
    {
        return projectProblem;
    }

    var occurredAt = clock.UtcNow;
    if (!HasRecentStepUp(admin.Principal!, occurredAt, adminOptions.Value))
    {
        await adminAuthentication.RecordSecurityAuditAsync(
            CreateAdminAuditEntry(
                httpContext,
                admin.Principal!,
                occurredAt,
                "admin.webhook_delivery.resend",
                "denied",
                "admin_step_up.required",
                "webhook_delivery",
                eventId.ToString("D"),
                projectId),
            cancellationToken);

        return IntegrationApiProblem.Create(
            httpContext,
            StatusCodes.Status403Forbidden,
            "Step-up authentication is required.",
            "admin_step_up.required");
    }

    var result = await webhookDelivery.ResendAsync(projectId ?? Guid.Empty, eventId, cancellationToken);
    var auditOutcome = result.Kind == WebhookManualResendResultKind.Resent ? "success" : "denied";
    var reasonCode = result.Kind switch
    {
        WebhookManualResendResultKind.Resent => "webhook_delivery.resent",
        WebhookManualResendResultKind.NotFound => "webhook_delivery.not_found",
        WebhookManualResendResultKind.NotResendable => "webhook_delivery.not_resendable",
        _ => throw new InvalidOperationException($"Unsupported webhook resend result {result.Kind}."),
    };
    await adminAuthentication.RecordSecurityAuditAsync(
        CreateAdminAuditEntry(
            httpContext,
            admin.Principal!,
            clock.UtcNow,
            "admin.webhook_delivery.resend",
            auditOutcome,
            reasonCode,
            "webhook_delivery",
            eventId.ToString("D"),
            projectId),
        cancellationToken);

    return result.Kind switch
    {
        WebhookManualResendResultKind.Resent =>
            Results.Ok(new AdminWebhookDeliveryResendHttpResponse("resent", result.Status!)),
        WebhookManualResendResultKind.NotFound =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status404NotFound,
                "Webhook Delivery was not found.",
                "webhook_delivery.not_found"),
        WebhookManualResendResultKind.NotResendable =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Webhook Delivery cannot be resent.",
                "webhook_delivery.not_resendable"),
        _ => throw new InvalidOperationException($"Unsupported webhook resend result {result.Kind}."),
    };
}

static async Task<IResult> LogoutAdminAsync(
    HttpContext httpContext,
    AdminAuthenticationService adminAuthentication,
    IAntiforgery antiforgery,
    CancellationToken cancellationToken)
{
    if (httpContext.Request.Cookies.ContainsKey(AdminSessionCookieName))
    {
        var csrfProblem = await ValidateAdminCsrfAsync(httpContext, antiforgery);
        if (csrfProblem is not null)
        {
            return csrfProblem;
        }
    }

    await adminAuthentication.LogoutAsync(
        new AdminLogoutCommand(
            httpContext.Request.Cookies[AdminSessionCookieName],
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString(),
            httpContext.TraceIdentifier),
        cancellationToken);

    httpContext.Response.Cookies.Delete(
        AdminSessionCookieName,
        new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });

    return Results.Ok(new AdminLogoutHttpResponse("logged_out"));
}

static async Task<AdminAuthenticationEndpointResult> AuthorizeSensitiveAdminMutationAsync(
    HttpContext httpContext,
    AdminAuthenticationService adminAuthentication,
    IAntiforgery antiforgery,
    IClock clock,
    AdminAuthenticationOptions adminOptions,
    string auditEventType,
    string subjectType,
    string subjectId,
    CancellationToken cancellationToken,
    Guid? projectId = null)
{
    if (httpContext.Request.Cookies.ContainsKey(AdminSessionCookieName))
    {
        var csrfProblem = await ValidateAdminCsrfAsync(httpContext, antiforgery);
        if (csrfProblem is not null)
        {
            return new AdminAuthenticationEndpointResult(Principal: null, csrfProblem);
        }
    }

    var admin = await AuthenticateAdminSessionAsync(httpContext, adminAuthentication, cancellationToken);
    if (admin.Result is not null)
    {
        return admin;
    }

    var occurredAt = clock.UtcNow;
    if (HasRecentStepUp(admin.Principal!, occurredAt, adminOptions))
    {
        return admin;
    }

    await adminAuthentication.RecordSecurityAuditAsync(
        CreateAdminAuditEntry(
            httpContext,
            admin.Principal!,
            occurredAt,
            auditEventType,
            "denied",
            "admin_step_up.required",
            subjectType,
            subjectId,
            projectId),
        cancellationToken);

    return new AdminAuthenticationEndpointResult(
        Principal: null,
        IntegrationApiProblem.Create(
            httpContext,
            StatusCodes.Status403Forbidden,
            "Step-up authentication is required.",
            "admin_step_up.required"));
}

static AdminOperationContext CreateAdminOperationContext(
    HttpContext httpContext,
    AdminSessionPrincipal admin) =>
    new(
        admin.AdminAccountId,
        httpContext.Connection.RemoteIpAddress?.ToString(),
        httpContext.Request.Headers.UserAgent.ToString(),
        httpContext.TraceIdentifier);

static IResult MapCredentialMutationResult(
    HttpContext httpContext,
    AdminIntegrationApiCredentialMutationResult result,
    bool includeToken)
{
    return result.Kind switch
    {
        AdminIntegrationApiCredentialMutationResultKind.Rotated when includeToken =>
            Results.Ok(new AdminIntegrationApiCredentialSecretHttpResponse(
                result.Credential!,
                result.Token!)),
        AdminIntegrationApiCredentialMutationResultKind.Disabled when !includeToken =>
            Results.Ok(new AdminIntegrationApiCredentialHttpResponse(result.Credential!)),
        AdminIntegrationApiCredentialMutationResultKind.InvalidVersion =>
            IntegrationApiProblem.Validation(
                httpContext,
                new Dictionary<string, string[]>
                {
                    ["expectedVersion"] = ["integration_api_credential.version.invalid"],
                }),
        AdminIntegrationApiCredentialMutationResultKind.NotFound =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status404NotFound,
                "Integration API Credential was not found.",
                "integration_api_credential.not_found"),
        AdminIntegrationApiCredentialMutationResultKind.ConcurrencyConflict =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Integration API Credential changed concurrently.",
                "integration_api_credential.concurrency_conflict",
                new Dictionary<string, object?>
                {
                    ["currentVersion"] = result.Credential!.Version,
                }),
        AdminIntegrationApiCredentialMutationResultKind.AlreadyDisabled =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Integration API Credential is disabled.",
                "integration_api_credential.disabled"),
        _ => throw new InvalidOperationException(
            $"Unsupported credential mutation result {result.Kind} for token response {includeToken}."),
    };
}

static IResult MapWebhookEndpointFailure(
    HttpContext httpContext,
    AdminWebhookEndpointResult result)
{
    return result.Kind switch
    {
        AdminWebhookEndpointResultKind.InvalidInput =>
            IntegrationApiProblem.Validation(
                httpContext,
                new Dictionary<string, string[]>
                {
                    ["request"] = ["webhook_endpoint.invalid"],
                }),
        AdminWebhookEndpointResultKind.SecretUnavailable =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Webhook Endpoint secret is unavailable.",
                "webhook_endpoint.secret_unavailable"),
        AdminWebhookEndpointResultKind.NotFound =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status404NotFound,
                "Webhook Endpoint was not found.",
                "webhook_endpoint.not_found"),
        AdminWebhookEndpointResultKind.ParentCredentialUnavailable =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Integration API Credential is unavailable.",
                "webhook_endpoint.integration_api_credential_unavailable"),
        AdminWebhookEndpointResultKind.ConcurrencyConflict =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Webhook Endpoint changed concurrently.",
                "webhook_endpoint.concurrency_conflict",
                new Dictionary<string, object?>
                {
                    ["currentVersion"] = result.Endpoint!.Version,
                }),
        AdminWebhookEndpointResultKind.AlreadyDisabled =>
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status409Conflict,
                "Webhook Endpoint is disabled.",
                "webhook_endpoint.disabled"),
        _ => throw new InvalidOperationException($"Unsupported webhook endpoint result {result.Kind}."),
    };
}

static IResult? ValidateProjectContext(HttpContext httpContext, Guid? projectId) =>
    projectId.GetValueOrDefault() == Guid.Empty
        ? IntegrationApiProblem.Validation(
            httpContext,
            new Dictionary<string, string[]> { ["projectId"] = ["project_id.required"] })
        : null;

static async Task<IResult?> ValidateAdminCsrfAsync(
    HttpContext httpContext,
    IAntiforgery antiforgery)
{
    try
    {
        await antiforgery.ValidateRequestAsync(httpContext);
        return null;
    }
    catch (AntiforgeryValidationException)
    {
        return IntegrationApiProblem.Create(
            httpContext,
            StatusCodes.Status403Forbidden,
            "Request is invalid.",
            "admin_csrf.invalid");
    }
}

static async Task<AdminAuthenticationEndpointResult> AuthenticateAdminSessionAsync(
    HttpContext httpContext,
    AdminAuthenticationService adminAuthentication,
    CancellationToken cancellationToken)
{
    var result = await adminAuthentication.AuthenticateSessionAsync(
        httpContext.Request.Cookies[AdminSessionCookieName],
        cancellationToken);

    return result.Kind == AdminSessionAuthenticationResultKind.Authenticated
        ? new AdminAuthenticationEndpointResult(result.Principal, Result: null)
        : new AdminAuthenticationEndpointResult(
            Principal: null,
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status401Unauthorized,
                "Authentication is invalid.",
                "admin_session.invalid"));
}

static bool HasRecentStepUp(
    AdminSessionPrincipal principal,
    DateTimeOffset occurredAt,
    AdminAuthenticationOptions options)
{
    // Null means the account has no second factor enrolled, so there is no
    // step-up to be fresh (ADR 0028). Treating that as "not fresh" would make
    // the sensitive operations unreachable for such an account rather than
    // protected, which is the opposite of what a gate is for.
    return principal.StepUpAuthenticatedAt is null
        || principal.StepUpAuthenticatedAt.Value.Add(options.StepUpLifetime) >= occurredAt;
}

static AdminAuditEntry CreateAdminAuditEntry(
    HttpContext httpContext,
    AdminSessionPrincipal admin,
    DateTimeOffset occurredAt,
    string eventType,
    string outcome,
    string reasonCode,
    string subjectType,
    string subjectId,
    Guid? projectId = null)
{
    return new AdminAuditEntry(
        Guid.NewGuid(),
        occurredAt,
        eventType,
        outcome,
        "product_user",
        admin.AdminAccountId.ToString("D"),
        "api",
        httpContext.Connection.RemoteIpAddress?.ToString(),
        httpContext.Request.Headers.UserAgent.ToString(),
        httpContext.TraceIdentifier,
        reasonCode,
        subjectType,
        subjectId,
        projectId);
}

static async Task<AuthenticationEndpointResult> AuthenticateAsync(
    HttpContext httpContext,
    IIntegrationApiCredentialAuthenticator authenticator,
    CancellationToken cancellationToken)
{
    var authorization = httpContext.Request.Headers.Authorization.ToString();
    if (string.IsNullOrWhiteSpace(authorization))
    {
        return new AuthenticationEndpointResult(
            null,
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status401Unauthorized,
                "Authentication is required.",
                "authentication.required"));
    }

    const string bearerPrefix = "Bearer ";
    if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
    {
        return new AuthenticationEndpointResult(
            null,
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status401Unauthorized,
                "Authentication is invalid.",
                "authentication.invalid"));
    }

    var token = authorization[bearerPrefix.Length..].Trim();
    if (token.Length == 0)
    {
        return new AuthenticationEndpointResult(
            null,
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status401Unauthorized,
                "Authentication is invalid.",
                "authentication.invalid"));
    }

    var principal = await authenticator.AuthenticateAsync(token, cancellationToken);
    return principal is null
        ? new AuthenticationEndpointResult(
            null,
            IntegrationApiProblem.Create(
                httpContext,
                StatusCodes.Status401Unauthorized,
                "Authentication is invalid.",
                "authentication.invalid"))
        : new AuthenticationEndpointResult(principal, Result: null);
}

static Dictionary<string, string[]> ValidateCreatePaymentRequest(
    CreatePaymentHttpRequest? request,
    string? idempotencyKey)
{
    var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

    if (request is null)
    {
        errors["body"] = ["request_body.required"];
        return errors;
    }

    AddIdempotencyKeyErrors(errors, idempotencyKey);
    AddFiatCurrencyErrors(errors, request.FiatCurrency);
    AddFiatAmountErrors(errors, request.FiatAmountMinor);
    AddExternalReferenceErrors(errors, request.ExternalReference);
    AddPaymentContextErrors(errors, request.PaymentContext);
    AddReturnUrlErrors(errors, request.ReturnUrl);

    return errors;
}

static void AddIdempotencyKeyErrors(
    IDictionary<string, string[]> errors,
    string? idempotencyKey)
{
    if (string.IsNullOrWhiteSpace(idempotencyKey))
    {
        errors["headers.idempotencyKey"] = ["idempotency_key.required"];
        return;
    }

    if (idempotencyKey.Trim().Length > 255)
    {
        errors["headers.idempotencyKey"] = ["idempotency_key.too_long"];
    }
}

static void AddFiatCurrencyErrors(
    IDictionary<string, string[]> errors,
    string? fiatCurrency)
{
    if (string.IsNullOrWhiteSpace(fiatCurrency))
    {
        errors["fiatCurrency"] = ["fiat_currency.required"];
        return;
    }

    var normalizedCurrency = fiatCurrency.Trim().ToUpperInvariant();
    if (normalizedCurrency is not ("EUR" or "USD"))
    {
        errors["fiatCurrency"] = ["fiat_currency.unsupported"];
    }
}

static void AddFiatAmountErrors(
    IDictionary<string, string[]> errors,
    long fiatAmountMinor)
{
    if (fiatAmountMinor <= 0)
    {
        errors["fiatAmountMinor"] = ["fiat_amount.not_positive"];
    }
}

static void AddExternalReferenceErrors(
    IDictionary<string, string[]> errors,
    string? externalReference)
{
    if (string.IsNullOrWhiteSpace(externalReference))
    {
        errors["externalReference"] = ["external_reference.required"];
        return;
    }

    if (externalReference.Trim().Length > 255)
    {
        errors["externalReference"] = ["external_reference.too_long"];
    }
}

static void AddPaymentContextErrors(
    IDictionary<string, string[]> errors,
    PaymentContextHttpRequest? paymentContext)
{
    if (paymentContext is null)
    {
        return;
    }

    AddContextFieldError(errors, "paymentContext.username", paymentContext.Username, "payment_context.username.too_long");
    AddContextFieldError(errors, "paymentContext.customerNumber", paymentContext.CustomerNumber, "payment_context.customer_number.too_long");
    AddContextFieldError(errors, "paymentContext.cartName", paymentContext.CartName, "payment_context.cart_name.too_long");
    AddContextFieldError(errors, "paymentContext.note", paymentContext.Note, "payment_context.note.too_long");
}

static void AddContextFieldError(
    IDictionary<string, string[]> errors,
    string fieldPath,
    string? value,
    string code)
{
    if (value is not null && value.Trim().Length > 255)
    {
        errors[fieldPath] = [code];
    }
}

static void AddReturnUrlErrors(
    IDictionary<string, string[]> errors,
    string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl))
    {
        return;
    }

    var trimmedUrl = returnUrl.Trim();
    if (trimmedUrl.Length > 2048 ||
        !Uri.TryCreate(trimmedUrl, UriKind.Absolute, out _))
    {
        errors["returnUrl"] = ["return_url.invalid"];
    }
}

static async Task<IResult> ReportClientErrorAsync(
    HttpContext httpContext,
    ILoggerFactory loggerFactory)
{
    // The body is read here rather than bound, so that the cap is enforced by
    // this endpoint on any server. Kestrel has a limit of its own, but it is
    // global and far larger, and a test host has none at all -- a bound that
    // only holds in production is a bound nobody has seen work.
    var request = await ClientErrorReport.ReadAsync(httpContext);
    if (request is null)
    {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    }

    // Everything below arrives from a browser nobody controls -- the payer page
    // is reachable by anyone -- so the shape of what is logged is decided here
    // and never by the caller. Fixed template, fixed fields, hard caps.
    var name = ClientErrorReport.SingleLine(request.Name, ClientErrorReport.MaxNameLength) ?? "Error";
    var message = ClientErrorReport.SingleLine(request.Message, ClientErrorReport.MaxMessageLength)
        ?? "(no message)";
    var path = ClientErrorReport.PathOnly(request.Path);
    var stack = ClientErrorReport.MultiLine(request.Stack, ClientErrorReport.MaxStackLength);

    // The name, the message and the stack are values, never part of the
    // template: a browser that reports `{Foo}` must not be able to write a
    // placeholder into the log.
    loggerFactory.CreateLogger("Payaffe.Web.ClientError").Log(
        // Warning, not Error, and this is a security property rather than a
        // judgement about severity. The endpoint is unauthenticated, so logging
        // at Error would let anyone raise the installation's error rate -- and
        // an error rate is what an alert is derived from.
        LogLevel.Warning,
        default,
        // Carried as an exception because that is the field a log store keeps a
        // stack trace in and searches on its own; a property that never reaches
        // the rendered message is stored but not findable.
        stack is null ? null : new ClientReportedError(stack),
        "Browser reported {ClientErrorName} on {ClientErrorPath}: {ClientErrorMessage}",
        [name, path, message]);

    // Accepted rather than created, and with no body: a page that is already
    // failing has nothing to do with the answer.
    return Results.Accepted();
}

/// <summary>
/// The caps and the scrubbing applied to a browser-reported error. Text from a
/// browser is untrusted, and it is read later by a person and by an agent, so
/// what leaves this class is single-line where it should be, bounded, and free
/// of control characters.
/// </summary>
public static class ClientErrorReport
{
    public const int MaxRequestBytes = 32 * 1024;

    /// <summary>
    /// Reads the report, or <c>null</c> when the caller sent more than
    /// <see cref="MaxRequestBytes"/>. Nothing beyond the cap is buffered: the
    /// read stops at the byte that crosses it.
    /// </summary>
    public static async Task<ClientErrorReportHttpRequest?> ReadAsync(HttpContext httpContext)
    {
        if (httpContext.Request.ContentLength > MaxRequestBytes)
        {
            return null;
        }

        using var buffered = new MemoryStream();
        var chunk = new byte[8 * 1024];
        while (true)
        {
            var read = await httpContext.Request.Body.ReadAsync(chunk, httpContext.RequestAborted);
            if (read == 0)
            {
                break;
            }

            if (buffered.Length + read > MaxRequestBytes)
            {
                return null;
            }

            buffered.Write(chunk, 0, read);
        }

        buffered.Position = 0;
        try
        {
            return await JsonSerializer.DeserializeAsync<ClientErrorReportHttpRequest>(
                buffered,
                JsonSerializerOptions.Web,
                httpContext.RequestAborted)
                ?? new ClientErrorReportHttpRequest(null, null, null, null);
        }
        catch (JsonException)
        {
            // A browser that cannot serialise its own failure is not worth a
            // 400 nobody will read. It is recorded as the empty report it is.
            return new ClientErrorReportHttpRequest(null, null, null, null);
        }
    }
    public const int MaxNameLength = 200;
    public const int MaxMessageLength = 1000;
    public const int MaxStackLength = 8000;
    public const int MaxPathLength = 200;

    /// <summary>
    /// One line, capped. Newlines are removed rather than escaped, because a
    /// reported message is a sentence and a message that spans lines is how a
    /// log reader is given something that looks like a second entry.
    /// </summary>
    public static string? SingleLine(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var character in value)
        {
            if (builder.Length == maxLength)
            {
                break;
            }

            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? null : Flag(result, value.Length > maxLength);
    }

    /// <summary>
    /// A stack trace keeps its line breaks, because that is what makes it a
    /// stack trace. Every other control character still goes.
    /// </summary>
    public static string? MultiLine(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var character in value)
        {
            if (builder.Length == maxLength)
            {
                break;
            }

            if (character is '\n' or '\t')
            {
                builder.Append(character);
            }
            else if (!char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? null : Flag(result, value.Length > maxLength);
    }

    /// <summary>
    /// The path of the page, and nothing else. A query string is dropped rather
    /// than trimmed of known keys: the payer page carries an identifier in its
    /// path and this endpoint has no business learning what else a URL held.
    /// </summary>
    public static string PathOnly(string? value)
    {
        var trimmed = SingleLine(value, 2048);
        if (trimmed is null)
        {
            return "(unknown)";
        }

        // Only an http(s) address is treated as one. A bare path such as
        // `/admin?tab=x` parses as an absolute `file:` URI on unix and its
        // AbsolutePath comes back percent-encoded, which is how `?` survived
        // as `%3F` the first time this was written.
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            trimmed = absolute.AbsolutePath;
        }

        var cut = trimmed.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            trimmed = trimmed[..cut];
        }

        trimmed = trimmed.Trim();
        if (trimmed.Length == 0)
        {
            return "(unknown)";
        }

        return trimmed.Length > MaxPathLength ? Flag(trimmed[..MaxPathLength], true) : trimmed;
    }

    private static string Flag(string value, bool truncated) => truncated ? value + "…" : value;
}

/// <summary>
/// A browser error is text, not something that was thrown here. This carries it
/// in the argument <c>ILogger</c> reserves for an exception, because that is
/// where a log store keeps a stack trace, and it renders as exactly the text it
/// was given -- no type name, no fabricated frames of our own.
/// </summary>
public sealed class ClientReportedError(string description) : Exception(description)
{
    public override string ToString() => Message;

    public override string? StackTrace => null;
}

public sealed class ClientErrorReportOptions
{
    /// <summary>
    /// False leaves the endpoint unmapped. There is then no publicly postable
    /// surface at all, rather than one that answers and discards.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public int RateLimitPermitLimit { get; set; } = 10;

    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// What a browser may report. Deliberately four fields: the release, the
/// environment, the user agent and the address are known to the host already,
/// and taking them from the caller would only let the caller choose them.
/// </summary>
public sealed record ClientErrorReportHttpRequest(
    string? Name,
    string? Message,
    string? Stack,
    string? Path);

public sealed record HealthResponse(string Status);

public sealed class IntegrationApiRateLimitOptions
{
    public int PermitLimit { get; set; } = 120;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed class CreatePaymentHttpRequest
{
    [Required]
    public string? FiatCurrency { get; init; }

    [Required]
    [Range(1, long.MaxValue)]
    public long FiatAmountMinor { get; init; }

    [Required]
    [MaxLength(255)]
    public string? ExternalReference { get; init; }

    public PaymentContextHttpRequest? PaymentContext { get; init; }

    [MaxLength(2048)]
    [Url]
    public string? ReturnUrl { get; init; }
}

public sealed class PaymentContextHttpRequest
{
    [MaxLength(255)]
    public string? Username { get; init; }

    [MaxLength(255)]
    public string? CustomerNumber { get; init; }

    [MaxLength(255)]
    public string? CartName { get; init; }

    [MaxLength(255)]
    public string? Note { get; init; }
}

public sealed record SelectPayerPaymentCurrencyHttpRequest(string? SupportedCurrency);

public sealed record SelectPaymentCurrencyHttpRequest(string? SupportedCurrency);

public sealed record AdminLoginStartHttpRequest(string? Username, string? Password);

public sealed record AdminLoginStartHttpResponse(string Status, Guid? ChallengeId);

public sealed record AdminMfaCompleteHttpRequest(Guid? ChallengeId, string? TotpCode, string? RecoveryCode);

public sealed record AdminMfaCompleteHttpResponse(string Status);

public sealed record AdminStepUpHttpRequest(string? TotpCode);

public sealed record AdminStepUpHttpResponse(
    string Status,
    DateTimeOffset? StepUpAuthenticatedAt,
    DateTimeOffset IdleExpiresAt);

public sealed record AdminAuditLogExportHttpRequest(Guid? ProjectId, int? Limit);

public sealed record AdminAuditLogExportHttpResponse(
    DateTimeOffset ExportedAt,
    IReadOnlyList<AdminAuditLogEntryDetailReadModel> Entries);

public sealed record AdminRecoveryCodesHttpResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<string> RecoveryCodes);

public sealed record AdminIntegrationApiCredentialCreateHttpRequest(Guid? ProjectId, string? Name);

public sealed record AdminIntegrationApiCredentialMutationHttpRequest(Guid? ProjectId, long? ExpectedVersion);

public sealed record AdminIntegrationApiCredentialsHttpResponse(
    IReadOnlyList<AdminIntegrationApiCredentialReadModel> Credentials);

public sealed record AdminIntegrationApiCredentialHttpResponse(
    AdminIntegrationApiCredentialReadModel Credential);

public sealed record AdminIntegrationApiCredentialSecretHttpResponse(
    AdminIntegrationApiCredentialReadModel Credential,
    string Token);

public sealed record AdminWebhookEndpointCreateHttpRequest(
    Guid? ProjectId,
    Guid? IntegrationApiCredentialId,
    string? Url,
    string? SecretReference,
    IReadOnlyList<string>? EventTypes);

public sealed record AdminWebhookEndpointUpdateHttpRequest(
    Guid? ProjectId,
    long? ExpectedVersion,
    string? Url,
    IReadOnlyList<string>? EventTypes);

public sealed record AdminWebhookEndpointSecretRotationHttpRequest(
    Guid? ProjectId,
    long? ExpectedVersion,
    string? SecretReference);

public sealed record AdminWebhookEndpointsHttpResponse(
    IReadOnlyList<AdminWebhookEndpointReadModel> Endpoints);

public sealed record AdminWebhookEndpointHttpResponse(AdminWebhookEndpointReadModel Endpoint);

public sealed record AdminNativeEthAddressPoolImportHttpRequest(Guid? ProjectId, IReadOnlyList<string>? Addresses);

public sealed record AdminNativeEthAddressPoolImportHttpResponse(
    Guid ImportId,
    int ImportedCount,
    AdminNativeEthAddressPoolSummary Summary);

public sealed record AdminWebhookDeliveryResendHttpResponse(string Status, string DeliveryStatus);

public sealed record AdminWebhookDeliveriesHttpResponse(IReadOnlyList<AdminWebhookDeliveryReadModel> Deliveries);

public sealed record AdminCsrfHttpResponse(string CsrfToken);

public sealed record AdminLogoutHttpResponse(string Status);

public sealed record AdminSessionHttpResponse(
    string Status,
    Guid AdminAccountId,
    string Username,
    DateTimeOffset? MfaAuthenticatedAt,
    DateTimeOffset? StepUpAuthenticatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset IdleExpiresAt);

public sealed record AdminPaymentsHttpResponse(IReadOnlyList<AdminPaymentSummaryReadModel> Payments);

public sealed record AdminPaymentSettlementHttpRequest(Guid? ProjectId, long? ExpectedVersion, string? Reason);

public sealed record AdminProjectCreateHttpRequest(string? Name, string? Slug);

public sealed record AdminProjectStatusHttpRequest(long? ExpectedVersion, string? Status);

public sealed record AdminProjectsHttpResponse(IReadOnlyList<AdminProjectReadModel> Projects);

public sealed record AdminProjectHttpResponse(AdminProjectReadModel Project);

public sealed record AdminReorgAlertsHttpResponse(IReadOnlyList<AdminReorgAlertReadModel> Alerts);

public sealed record AdminObservationHealthHttpResponse(
    IReadOnlyList<ObservationHealthReadModel> Currencies);

public sealed record AdminAuditLogHttpResponse(IReadOnlyList<AdminAuditLogEntryReadModel> Entries);

public sealed record AdminAuthenticationEndpointResult(
    AdminSessionPrincipal? Principal,
    IResult? Result);

public sealed record AuthenticationEndpointResult(
    AuthenticatedIntegrationApiCredential? Principal,
    IResult? Result);

public sealed class IntegrationApiProblemResponse : ProblemDetails
{
    [Required]
    public string? Code { get; init; }

    [Required]
    public string? CorrelationId { get; init; }

    public Dictionary<string, string[]>? Errors { get; init; }
}

public static class IntegrationApiProblem
{
    public static IResult Validation(
        HttpContext httpContext,
        IReadOnlyDictionary<string, string[]> errors)
    {
        return Create(
            httpContext,
            StatusCodes.Status400BadRequest,
            "Validation failed.",
            "validation.failed",
            new Dictionary<string, object?>
            {
                ["errors"] = errors,
            });
    }

    public static IResult Create(
        HttpContext httpContext,
        int statusCode,
        string title,
        string code,
        IDictionary<string, object?>? extensions = null)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
        };

        problemDetails.Extensions["code"] = code;
        problemDetails.Extensions["correlationId"] = httpContext.TraceIdentifier;

        if (extensions is not null)
        {
            foreach (var extension in extensions)
            {
                problemDetails.Extensions[extension.Key] = extension.Value;
            }
        }

        return Results.Json(problemDetails, statusCode: statusCode, contentType: "application/problem+json");
    }
}

public partial class Program;
