namespace Payaffe.Application.Admin;

public sealed record AdminIntegrationApiCredentialCreateResult(
    AdminIntegrationApiCredentialCreateResultKind Kind,
    AdminIntegrationApiCredentialReadModel? Credential,
    string? Token)
{
    public static AdminIntegrationApiCredentialCreateResult Created(
        AdminIntegrationApiCredentialReadModel credential,
        string token) =>
        new(AdminIntegrationApiCredentialCreateResultKind.Created, credential, token);

    public static AdminIntegrationApiCredentialCreateResult InvalidName() =>
        new(AdminIntegrationApiCredentialCreateResultKind.InvalidName, Credential: null, Token: null);

    public static AdminIntegrationApiCredentialCreateResult ProjectUnavailable() =>
        new(AdminIntegrationApiCredentialCreateResultKind.ProjectUnavailable, Credential: null, Token: null);
}

public enum AdminIntegrationApiCredentialCreateResultKind
{
    Created,
    InvalidName,
    ProjectUnavailable,
}

public sealed record AdminIntegrationApiCredentialMutationResult(
    AdminIntegrationApiCredentialMutationResultKind Kind,
    AdminIntegrationApiCredentialReadModel? Credential,
    string? Token)
{
    public static AdminIntegrationApiCredentialMutationResult Rotated(
        AdminIntegrationApiCredentialReadModel credential,
        string token) =>
        new(AdminIntegrationApiCredentialMutationResultKind.Rotated, credential, token);

    public static AdminIntegrationApiCredentialMutationResult Disabled(
        AdminIntegrationApiCredentialReadModel credential) =>
        new(AdminIntegrationApiCredentialMutationResultKind.Disabled, credential, Token: null);

    public static AdminIntegrationApiCredentialMutationResult NotFound() =>
        new(AdminIntegrationApiCredentialMutationResultKind.NotFound, Credential: null, Token: null);

    public static AdminIntegrationApiCredentialMutationResult InvalidVersion() =>
        new(AdminIntegrationApiCredentialMutationResultKind.InvalidVersion, Credential: null, Token: null);

    public static AdminIntegrationApiCredentialMutationResult ConcurrencyConflict(
        AdminIntegrationApiCredentialReadModel credential) =>
        new(AdminIntegrationApiCredentialMutationResultKind.ConcurrencyConflict, credential, Token: null);

    public static AdminIntegrationApiCredentialMutationResult AlreadyDisabled(
        AdminIntegrationApiCredentialReadModel credential) =>
        new(AdminIntegrationApiCredentialMutationResultKind.AlreadyDisabled, credential, Token: null);
}

public enum AdminIntegrationApiCredentialMutationResultKind
{
    Rotated,
    Disabled,
    NotFound,
    InvalidVersion,
    ConcurrencyConflict,
    AlreadyDisabled,
}
