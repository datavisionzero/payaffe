using System.ComponentModel.DataAnnotations;
using Payaffe.Worker;

namespace Payaffe.Worker.Tests;

/// <summary>
/// The worker host has no HTTP endpoint, so the heartbeat file is the whole
/// contract between the running host and the container healthcheck.
/// </summary>
public sealed class WorkerHeartbeatFileTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T10:00:00Z");

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"payaffe-worker-tests-{Guid.NewGuid():N}");

    private string HeartbeatPath => Path.Combine(_directory, "health");

    [Fact]
    public async Task Written_heartbeat_reads_back_as_the_same_instant()
    {
        var file = new WorkerHeartbeatFile(HeartbeatPath);

        await file.WriteAsync(Now, CancellationToken.None);

        Assert.Equal(Now, await file.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Writing_creates_the_containing_directory()
    {
        var file = new WorkerHeartbeatFile(Path.Combine(_directory, "nested", "health"));

        await file.WriteAsync(Now, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_directory, "nested", "health")));
    }

    [Fact]
    public async Task Repeated_writes_leave_no_temporary_file_behind()
    {
        var file = new WorkerHeartbeatFile(HeartbeatPath);

        await file.WriteAsync(Now, CancellationToken.None);
        await file.WriteAsync(Now.AddSeconds(15), CancellationToken.None);

        Assert.Equal([HeartbeatPath], Directory.GetFiles(_directory));
        Assert.Equal(Now.AddSeconds(15), await file.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Missing_heartbeat_reads_as_null()
    {
        var file = new WorkerHeartbeatFile(HeartbeatPath);

        Assert.Null(await file.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Unparsable_heartbeat_reads_as_null()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(HeartbeatPath, "not-a-timestamp");
        var file = new WorkerHeartbeatFile(HeartbeatPath);

        Assert.Null(await file.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public void Absent_heartbeat_is_missing()
    {
        Assert.Equal(
            WorkerHealthState.Missing,
            WorkerHeartbeatFile.Evaluate(heartbeat: null, Now, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Recent_heartbeat_is_healthy()
    {
        Assert.Equal(
            WorkerHealthState.Healthy,
            WorkerHeartbeatFile.Evaluate(Now.AddSeconds(-30), Now, TimeSpan.FromMinutes(1)));
    }

    /// <summary>Exactly at the maximum age the host still counts as healthy.</summary>
    [Fact]
    public void Heartbeat_at_the_maximum_age_is_healthy()
    {
        Assert.Equal(
            WorkerHealthState.Healthy,
            WorkerHeartbeatFile.Evaluate(Now.AddMinutes(-1), Now, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Heartbeat_past_the_maximum_age_is_stale()
    {
        Assert.Equal(
            WorkerHealthState.Stale,
            WorkerHeartbeatFile.Evaluate(Now.AddMinutes(-1).AddSeconds(-1), Now, TimeSpan.FromMinutes(1)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

public sealed class WorkerHealthOptionsTests
{
    [Fact]
    public void Shipped_defaults_are_valid()
    {
        Assert.Empty(Validate(new WorkerHealthOptions()));
    }

    /// <summary>
    /// The heartbeat is refreshed once per probe, so a maximum age that is not
    /// longer than the probe interval can never be satisfied.
    /// </summary>
    [Fact]
    public void Rejects_a_maximum_age_the_probe_can_never_satisfy()
    {
        var results = Validate(new WorkerHealthOptions
        {
            ProbeInterval = TimeSpan.FromSeconds(30),
            MaxAge = TimeSpan.FromSeconds(30),
        });

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(WorkerHealthOptions.MaxAge)));
    }

    [Fact]
    public void Rejects_a_probe_interval_outside_the_supported_bounds()
    {
        var results = Validate(new WorkerHealthOptions
        {
            ProbeInterval = TimeSpan.FromMinutes(10),
            MaxAge = TimeSpan.FromMinutes(20),
        });

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(WorkerHealthOptions.ProbeInterval)));
    }

    [Fact]
    public void Rejects_a_blank_heartbeat_path()
    {
        var results = Validate(new WorkerHealthOptions { HeartbeatPath = "  " });

        Assert.Contains(results, result => result.MemberNames.Contains(nameof(WorkerHealthOptions.HeartbeatPath)));
    }

    private static List<ValidationResult> Validate(WorkerHealthOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
