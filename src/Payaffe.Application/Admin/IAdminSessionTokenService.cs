namespace Payaffe.Application.Admin;

public interface IAdminSessionTokenService
{
    string GenerateToken();

    string HashToken(string token);
}
