using Payaffe.Application;
using Payaffe.Application.Payments;
using Payaffe.Migrations;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Payaffe")
    ?? throw new InvalidOperationException("Connection string 'Payaffe' is required for migration runs.");

builder.Services.Configure<PaymentApplicationOptions>(builder.Configuration.GetSection("Payments"));
builder.Services.Configure<PaymentAddressOptions>(builder.Configuration.GetSection("PaymentAddresses"));
builder.Services.AddPayaffeInstallationMode(builder.Configuration);
builder.Services.AddPayaffeApplication();
builder.Services.AddPayaffeInfrastructure(connectionString);
builder.Services.AddScoped<AdminBootstrapService>();

using var host = builder.Build();
if (args.Length == 0 || string.Equals(args[0], "migrate", StringComparison.OrdinalIgnoreCase))
{
    await MigrationRunner.ApplyAsync(host.Services, CancellationToken.None);
    return 0;
}

if (!string.Equals(args[0], "bootstrap-admin", StringComparison.OrdinalIgnoreCase) ||
    !TryGetOption(args, "--username", out var username))
{
    Console.Error.WriteLine(
        "Usage: Payaffe.Migrations bootstrap-admin --username <name> [--credentials-file <path>]");
    return 2;
}

// With a credentials file the command needs no terminal: it generates the
// password and writes it, with the Recovery Codes, to that file and prints
// nothing secret (ADR 0037). Without one, a person types the password.
AdminCredentialsFile? credentialsFile = null;
if (TryGetOption(args, "--credentials-file", out var credentialsPath))
{
    try
    {
        credentialsFile = AdminCredentialsFile.CreateNew(credentialsPath);
    }
    catch (IOException) when (File.Exists(credentialsPath))
    {
        Console.Error.WriteLine(
            $"{credentialsPath} already exists and is not overwritten; it may hold an earlier run's credentials. Nothing was created.");
        return 2;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(
            $"{credentialsPath} cannot be created: {exception.Message} Mount a directory the container's user can write, " +
            "for example with --user \"$(id -u):$(id -g)\". Nothing was created.");
        return 2;
    }
}
else if (Console.IsInputRedirected || Console.IsOutputRedirected)
{
    Console.Error.WriteLine(
        "Admin bootstrap requires an interactive terminal, or --credentials-file <path> to run without one.");
    return 2;
}

// The schema is applied first, so the first admin can be created before any
// host has started, or while one is still migrating: both wait on the same
// advisory lock.
await MigrationRunner.ApplyAsync(host.Services, CancellationToken.None);

string password;
if (credentialsFile is not null)
{
    password = AdminCredentialsFile.GeneratePassword();
}
else
{
    password = ReadSecret($"Password (at least {AdminBootstrapService.MinimumPasswordLength} characters): ");
    if (password.Length < AdminBootstrapService.MinimumPasswordLength)
    {
        Console.Error.WriteLine(
            $"The password has to be at least {AdminBootstrapService.MinimumPasswordLength} characters.");
        return 2;
    }

    var passwordConfirmation = ReadSecret("Confirm password: ");
    if (!string.Equals(password, passwordConfirmation, StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Password confirmation did not match.");
        return 2;
    }
}

using var scope = host.Services.CreateScope();
var bootstrapService = scope.ServiceProvider.GetRequiredService<AdminBootstrapService>();
AdminBootstrapResult result;
try
{
    result = await bootstrapService.BootstrapAsync(
        new AdminBootstrapRequest(
            username,
            password,
            Guid.NewGuid().ToString("D")),
        CancellationToken.None);
}
catch
{
    credentialsFile?.Discard();
    throw;
}

if (result.Status != AdminBootstrapStatus.Created)
{
    credentialsFile?.Discard();
}

switch (result.Status)
{
    case AdminBootstrapStatus.Created when credentialsFile is not null:
        try
        {
            using (credentialsFile)
            {
                credentialsFile.Write(
                    result.AdminAccountId!.Value,
                    username.Trim(),
                    password,
                    result.RecoveryCodes,
                    DateTimeOffset.UtcNow);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The file was created and held open, so this is a full disk or a
            // vanished mount, not a path problem. The account exists and its
            // password went nowhere, and bootstrap will not run a second time.
            Console.Error.WriteLine(
                $"Admin Account {result.AdminAccountId:D} was created, but {credentialsFile.Path} could not be written: {exception.Message}");
            Console.Error.WriteLine(
                "Its password is lost. An installation with nothing else in it yet starts again from an empty database.");
            return 4;
        }

        Console.WriteLine($"Admin Account created: {result.AdminAccountId:D}");
        Console.WriteLine($"Its password and Recovery Codes are in {credentialsFile.Path}, readable by its owner only.");
        Console.WriteLine("Nothing secret was printed. Whoever signs in reads that file, moves both into a");
        Console.WriteLine("password manager, and deletes it.");
        return 0;
    case AdminBootstrapStatus.Created:
        Console.WriteLine($"Admin Account created: {result.AdminAccountId:D}");
        Console.WriteLine();
        Console.WriteLine("Recovery Codes (shown once):");
        foreach (var recoveryCode in result.RecoveryCodes)
        {
            Console.WriteLine($"  {recoveryCode}");
        }

        Console.WriteLine();
        Console.WriteLine("Store them somewhere that is not this host. They are the way back in");
        Console.WriteLine("if the password is lost.");
        Console.WriteLine();
        Console.WriteLine("This account signs in with its password alone. Adding a second factor");
        Console.WriteLine("is yours to do later, from the Admin UI.");
        return 0;
    case AdminBootstrapStatus.AlreadyBootstrapped:
        Console.Error.WriteLine("Admin bootstrap is no longer available for this installation.");
        return 3;
    default:
        Console.Error.WriteLine(
            $"The bootstrap input was invalid: a username is required, and a password of at least {AdminBootstrapService.MinimumPasswordLength} characters.");
        return 2;
}

static bool TryGetOption(string[] arguments, string optionName, out string value)
{
    for (var index = 1; index < arguments.Length - 1; index++)
    {
        if (!string.Equals(arguments[index], optionName, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        value = arguments[index + 1];
        return !string.IsNullOrWhiteSpace(value);
    }

    value = string.Empty;
    return false;
}

static string ReadSecret(string prompt)
{
    Console.Write(prompt);
    var characters = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return new string([.. characters]);
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (characters.Count > 0)
            {
                characters.RemoveAt(characters.Count - 1);
            }

            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            characters.Add(key.KeyChar);
        }
    }
}
