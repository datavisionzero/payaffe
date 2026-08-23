namespace Payaffe.Application.Admin;

public interface IAdminTotpVerifier
{
    bool VerifyCode(
        byte[] secret,
        string code,
        DateTimeOffset now,
        int allowedTimeStepSkew);
}
