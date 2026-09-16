namespace Payaffe.Application.Payments;

public sealed record AuthenticatedIntegrationApiCredential(
    Guid Id,
    Guid ProjectId,
    string ProjectStatus,
    string Name);
