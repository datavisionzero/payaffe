namespace Payaffe.Application.Admin;

public interface IIntegrationApiCredentialTokenService
{
    string GenerateToken();

    string HashToken(string token);
}
