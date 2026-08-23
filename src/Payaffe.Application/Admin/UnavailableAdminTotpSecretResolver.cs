namespace Payaffe.Application.Admin;

public sealed class UnavailableAdminTotpSecretResolver : IAdminTotpSecretResolver
{
    public Task<byte[]?> ResolveSecretAsync(
        string secretReference,
        CancellationToken cancellationToken) =>
        Task.FromResult<byte[]?>(null);
}
