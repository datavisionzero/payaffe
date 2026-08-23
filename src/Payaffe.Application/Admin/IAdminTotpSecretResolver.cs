namespace Payaffe.Application.Admin;

public interface IAdminTotpSecretResolver
{
    Task<byte[]?> ResolveSecretAsync(
        string secretReference,
        CancellationToken cancellationToken);
}
