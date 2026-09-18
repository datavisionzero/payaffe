using Microsoft.EntityFrameworkCore;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Persistence;

namespace Payaffe.Infrastructure.Payments;

public sealed class EfProjectPaymentConfigurationStore(PayaffeDbContext dbContext)
    : IProjectPaymentConfigurationStore
{
    public async Task<ProjectPaymentConfiguration?> FindByCredentialAsync(
        Guid integrationApiCredentialId,
        CancellationToken cancellationToken)
    {
        var projectId = await dbContext.IntegrationApiCredentials
            .AsNoTracking()
            .Where(credential => credential.Id == integrationApiCredentialId)
            .Select(credential => (Guid?)credential.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);

        return projectId is null
            ? null
            : await FindByProjectAsync(projectId.Value, cancellationToken);
    }

    public async Task<ProjectPaymentConfiguration?> FindByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var record = await (
                from project in dbContext.Projects.AsNoTracking()
                join projectConfiguration in dbContext.ProjectConfigurations.AsNoTracking()
                    on project.Id equals projectConfiguration.ProjectId
                where project.Id == projectId
                select new { project.Status, Configuration = projectConfiguration })
            .SingleOrDefaultAsync(cancellationToken);

        if (record is null)
        {
            return null;
        }

        var configuration = record.Configuration;
        return new ProjectPaymentConfiguration(
            configuration.ProjectId,
            record.Status,
            TimeSpan.FromSeconds(configuration.PaymentExpirationSeconds),
            TimeSpan.FromSeconds(configuration.LateAcceptanceWindowSeconds),
            configuration.PaymentTolerancePercent,
            new ProjectCurrencyConfiguration(
                configuration.BtcEnabled,
                configuration.BtcConfirmationRequirement,
                configuration.BtcReorgMonitoringDepth),
            new ProjectCurrencyConfiguration(
                configuration.LtcEnabled,
                configuration.LtcConfirmationRequirement,
                configuration.LtcReorgMonitoringDepth),
            new ProjectCurrencyConfiguration(
                configuration.EthEnabled,
                configuration.EthConfirmationRequirement,
                configuration.EthReorgMonitoringDepth));
    }
}
