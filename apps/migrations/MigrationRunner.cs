using Payaffe.Infrastructure.Persistence;

namespace Payaffe.Migrations;

/// <summary>
/// The manual schema run: <c>docker compose run --rm migrations migrate</c>.
/// </summary>
/// <remarks>
/// Since ADR 0027 the hosts apply the schema themselves as they start, so this
/// is no longer a required step in an upgrade. It stays because applying a
/// schema before starting anything is still a reasonable thing for an operator
/// to want — and because it is the same code path, not a second one that could
/// drift from it.
/// </remarks>
public static class MigrationRunner
{
    public static Task ApplyAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken) =>
        SchemaMigrator.ApplyAsync(serviceProvider, cancellationToken);
}
