namespace Payaffe.Infrastructure.Persistence;

public enum SchemaMigrationStatus
{
    /// <summary>The host has not finished starting the migration yet.</summary>
    Pending,
    Applying,
    Complete,

    /// <summary>
    /// The migration threw. The host is on its way down; this exists so that
    /// readiness can answer honestly during the seconds before it is.
    /// </summary>
    Failed,
}

/// <summary>
/// Whether this process has finished bringing the schema up to date.
/// </summary>
/// <remarks>
/// Readiness reads <see cref="IsComplete"/> and the scheduled workers await
/// <see cref="Completion"/>. Both are needed because the migration does not
/// block the host from starting (ADR 0027): an API that refused to start
/// while PostgreSQL was briefly away would take `/health/live` down with it,
/// and a liveness endpoint that fails when a dependency does is not one.
/// </remarks>
public sealed class SchemaMigrationState
{
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _status = (int)SchemaMigrationStatus.Pending;

    public SchemaMigrationState()
    {
        // Observed here so that a failure nobody happened to await does not
        // surface later as an unobserved task exception, which would be
        // reported far from the migration that caused it.
        _ = _completion.Task.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// The state for a host that does not migrate on startup — the local Admin
    /// MCP host, the <c>migrations</c> host, and the tests. They take the
    /// schema as given, so the workers must not wait for a migration that is
    /// never going to run here.
    /// </summary>
    public static SchemaMigrationState AlreadyApplied()
    {
        var state = new SchemaMigrationState();
        state.Complete();
        return state;
    }

    public SchemaMigrationStatus Status => (SchemaMigrationStatus)Volatile.Read(ref _status);

    public bool IsComplete => Status == SchemaMigrationStatus.Complete;

    /// <summary>
    /// Completes when the schema is current, and faults when applying it
    /// failed.
    /// </summary>
    public Task Completion => _completion.Task;

    internal void Applying() => Volatile.Write(ref _status, (int)SchemaMigrationStatus.Applying);

    internal void Complete()
    {
        Volatile.Write(ref _status, (int)SchemaMigrationStatus.Complete);
        _completion.TrySetResult();
    }

    internal void Fail(Exception exception)
    {
        Volatile.Write(ref _status, (int)SchemaMigrationStatus.Failed);
        _completion.TrySetException(exception);
    }
}
