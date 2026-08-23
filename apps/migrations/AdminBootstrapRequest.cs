namespace Payaffe.Migrations;

public sealed record AdminBootstrapRequest(
    string Username,
    string Password,
    string TotpSecretReference,
    string TotpCode,
    string CorrelationId);
