namespace Payaffe.Application.Admin;

public interface IAdminPasswordHasher
{
    string HashPassword(string password);

    bool VerifyPassword(string password, string passwordHash);
}
