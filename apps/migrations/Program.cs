using Payaffe.Application;
using Payaffe.Migrations;
using Payaffe.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Payaffe")
    ?? throw new InvalidOperationException("Connection string 'Payaffe' is required for migration runs.");

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
    Console.Error.WriteLine("Usage: Payaffe.Migrations bootstrap-admin --username <name>");
    return 2;
}

if (Console.IsInputRedirected || Console.IsOutputRedirected)
{
    Console.Error.WriteLine("Admin bootstrap requires an interactive terminal.");
    return 2;
}

var password = ReadSecret("Password: ");
var passwordConfirmation = ReadSecret("Confirm password: ");
if (!string.Equals(password, passwordConfirmation, StringComparison.Ordinal))
{
    Console.Error.WriteLine("Password confirmation did not match.");
    return 2;
}

using var scope = host.Services.CreateScope();
var bootstrapService = scope.ServiceProvider.GetRequiredService<AdminBootstrapService>();
var result = await bootstrapService.BootstrapAsync(
    new AdminBootstrapRequest(
        username,
        password,
        Guid.NewGuid().ToString("D")),
    CancellationToken.None);

switch (result.Status)
{
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
        Console.Error.WriteLine("The bootstrap input was invalid.");
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
