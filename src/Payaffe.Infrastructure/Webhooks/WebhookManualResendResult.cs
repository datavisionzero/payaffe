namespace Payaffe.Infrastructure.Webhooks;

public sealed record WebhookManualResendResult(
    WebhookManualResendResultKind Kind,
    string? Status)
{
    public static WebhookManualResendResult Resent(string status) =>
        new(WebhookManualResendResultKind.Resent, status);

    public static WebhookManualResendResult NotFound() =>
        new(WebhookManualResendResultKind.NotFound, Status: null);

    public static WebhookManualResendResult NotResendable(string status) =>
        new(WebhookManualResendResultKind.NotResendable, status);

    public static WebhookManualResendResult InProgress() =>
        new(WebhookManualResendResultKind.InProgress, Status: null);
}

public enum WebhookManualResendResultKind
{
    Resent,
    NotFound,
    NotResendable,

    /// <summary>A worker or another resend holds the event lease right now.</summary>
    InProgress,
}
