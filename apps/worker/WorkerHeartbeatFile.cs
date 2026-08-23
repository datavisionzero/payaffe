using System.Globalization;

namespace Payaffe.Worker;

public enum WorkerHealthState
{
    /// <summary>The host refreshed the heartbeat recently enough.</summary>
    Healthy,

    /// <summary>No heartbeat exists yet, or it could not be read.</summary>
    Missing,

    /// <summary>The heartbeat exists but is older than the configured maximum age.</summary>
    Stale,
}

/// <summary>
/// The heartbeat file shared between the running worker host and the
/// short-lived `health` command that the container healthcheck runs.
/// </summary>
public sealed class WorkerHeartbeatFile(string path)
{
    public string Path => path;

    /// <summary>
    /// Writes through a temporary file and one move, so the reading process
    /// never observes a partially written heartbeat.
    /// </summary>
    public async Task WriteAsync(DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{path}.{Environment.ProcessId}.tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public async Task<DateTimeOffset?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var content = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
            return DateTimeOffset.TryParse(
                content,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp)
                ? timestamp
                : null;
        }
        catch (IOException)
        {
            // An unreadable heartbeat is reported as missing rather than
            // failing the healthcheck process with an exception.
            return null;
        }
    }

    public static WorkerHealthState Evaluate(DateTimeOffset? heartbeat, DateTimeOffset now, TimeSpan maxAge)
    {
        if (heartbeat is null)
        {
            return WorkerHealthState.Missing;
        }

        return now - heartbeat.Value > maxAge ? WorkerHealthState.Stale : WorkerHealthState.Healthy;
    }
}
