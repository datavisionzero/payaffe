namespace Payaffe.Migrations;

/// <summary>
/// What creating the first Admin Account needs, which since ADR 0028 is a
/// username and a password.
/// </summary>
/// <remarks>
/// The TOTP secret reference and the code that proved it are gone. They made
/// the first account depend on a value that had to be agreed between an
/// operator, a secret store and an authenticator app before any account
/// existed — and a setup step that can fail before it has produced anything is
/// the one people abandon.
/// </remarks>
public sealed record AdminBootstrapRequest(
    string Username,
    string Password,
    string CorrelationId);
