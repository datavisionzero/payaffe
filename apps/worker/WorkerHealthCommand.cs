using Microsoft.Extensions.Configuration;

namespace Payaffe.Worker;

/// <summary>
/// The `health` subcommand the container healthcheck runs. It reads the same
/// configuration as the host but touches neither the database nor the
/// telemetry pipeline, so it stays cheap enough to run on a short interval.
/// </summary>
public static class WorkerHealthCommand
{
    public const string CommandName = "health";

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = configuration.GetSection("Workers:Health").Get<WorkerHealthOptions>()
            ?? new WorkerHealthOptions();

        var heartbeatFile = new WorkerHeartbeatFile(options.HeartbeatPath);
        var heartbeat = await heartbeatFile.ReadAsync(cancellationToken);
        var state = WorkerHeartbeatFile.Evaluate(heartbeat, DateTimeOffset.UtcNow, options.MaxAge);
        if (state is WorkerHealthState.Healthy)
        {
            return 0;
        }

        await Console.Error.WriteLineAsync(state is WorkerHealthState.Missing
            ? $"Worker host has not written a heartbeat to {options.HeartbeatPath}."
            : $"Worker host heartbeat is older than {options.MaxAge}.");
        return 1;
    }
}
