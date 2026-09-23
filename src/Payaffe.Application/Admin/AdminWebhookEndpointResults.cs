namespace Payaffe.Application.Admin;

public sealed record AdminWebhookEndpointResult(
    AdminWebhookEndpointResultKind Kind,
    AdminWebhookEndpointReadModel? Endpoint)
{
    public static AdminWebhookEndpointResult Success(AdminWebhookEndpointReadModel endpoint) =>
        new(AdminWebhookEndpointResultKind.Success, endpoint);

    public static AdminWebhookEndpointResult InvalidInput() =>
        new(AdminWebhookEndpointResultKind.InvalidInput, Endpoint: null);

    public static AdminWebhookEndpointResult TargetNotPublic() =>
        new(AdminWebhookEndpointResultKind.TargetNotPublic, Endpoint: null);

    public static AdminWebhookEndpointResult SecretUnavailable() =>
        new(AdminWebhookEndpointResultKind.SecretUnavailable, Endpoint: null);

    public static AdminWebhookEndpointResult NotFound() =>
        new(AdminWebhookEndpointResultKind.NotFound, Endpoint: null);

    public static AdminWebhookEndpointResult ParentCredentialUnavailable() =>
        new(AdminWebhookEndpointResultKind.ParentCredentialUnavailable, Endpoint: null);

    public static AdminWebhookEndpointResult ConcurrencyConflict(AdminWebhookEndpointReadModel endpoint) =>
        new(AdminWebhookEndpointResultKind.ConcurrencyConflict, endpoint);

    public static AdminWebhookEndpointResult AlreadyDisabled(AdminWebhookEndpointReadModel endpoint) =>
        new(AdminWebhookEndpointResultKind.AlreadyDisabled, endpoint);
}

public enum AdminWebhookEndpointResultKind
{
    Success,
    InvalidInput,
    TargetNotPublic,
    SecretUnavailable,
    NotFound,
    ParentCredentialUnavailable,
    ConcurrencyConflict,
    AlreadyDisabled,
}
