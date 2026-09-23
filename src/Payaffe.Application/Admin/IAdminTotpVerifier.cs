namespace Payaffe.Application.Admin;

public interface IAdminTotpVerifier
{
    /// <summary>
    /// Checks <paramref name="code"/> against the time steps within the
    /// allowed skew of <paramref name="now"/>.
    /// </summary>
    /// <param name="timeStep">
    /// The RFC 6238 time step the code matched, so the caller can refuse a
    /// step it has already accepted.
    /// </param>
    bool TryVerifyCode(
        byte[] secret,
        string code,
        DateTimeOffset now,
        int allowedTimeStepSkew,
        out long timeStep);
}
