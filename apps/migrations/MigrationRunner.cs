using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Migrations;

public static class MigrationRunner
{
    public static async Task ApplyAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();

        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
