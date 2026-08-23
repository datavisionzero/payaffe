using Payaffe.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Payaffe.Api.Tests;

internal static class SchemaMigrationTestSetup
{
    /// <summary>
    /// Takes the startup migration out of a test host (ADR 0027).
    /// </summary>
    /// <remarks>
    /// These factories either replace the product database with an in-memory
    /// one or point at no database at all, so there is no schema for the
    /// migration to apply and its failure would stop the host mid-test. What
    /// is under test here is the API surface, not the migration; that has its
    /// own tests against real PostgreSQL.
    /// </remarks>
    public static void RemoveStartupSchemaMigration(this IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(candidate =>
            candidate.ImplementationType == typeof(SchemaMigrationHostedService));
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }

        services.RemoveAll<SchemaMigrationState>();
        services.AddSingleton(SchemaMigrationState.AlreadyApplied());
    }
}
