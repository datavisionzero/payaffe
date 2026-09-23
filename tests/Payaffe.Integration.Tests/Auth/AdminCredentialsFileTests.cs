using Payaffe.Migrations;

namespace Payaffe.Integration.Tests.Auth;

/// <summary>
/// The file an unattended bootstrap leaves the first admin's secrets in
/// (ADR 0037).
/// </summary>
public sealed class AdminCredentialsFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("payaffe-credentials-").FullName;

    [Fact]
    public void Holds_the_password_and_every_recovery_code()
    {
        var path = Path.Combine(_directory, "first-admin.txt");
        var accountId = Guid.NewGuid();
        var password = AdminCredentialsFile.GeneratePassword();

        using (var file = AdminCredentialsFile.CreateNew(path))
        {
            file.Write(
                accountId,
                "admin@example.test",
                password,
                ["AAAA-BBBBB-CCCC", "DDDD-EEEEE-FFFF"],
                DateTimeOffset.UnixEpoch);
        }

        var content = File.ReadAllText(path);
        Assert.Contains($"Admin Account id: {accountId:D}", content);
        Assert.Contains("Username:         admin@example.test", content);
        Assert.Contains($"Password:         {password}", content);
        Assert.Contains("  AAAA-BBBBB-CCCC", content);
        Assert.Contains("  DDDD-EEEEE-FFFF", content);
    }

    [Fact]
    public void Is_readable_by_its_owner_only()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_directory, "first-admin.txt");

        using (AdminCredentialsFile.CreateNew(path))
        {
        }

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(path));
    }

    [Fact]
    public void Refuses_to_overwrite_an_existing_file()
    {
        var path = Path.Combine(_directory, "first-admin.txt");
        File.WriteAllText(path, "an earlier run");

        Assert.Throws<IOException>(() => AdminCredentialsFile.CreateNew(path));
        Assert.Equal("an earlier run", File.ReadAllText(path));
    }

    [Fact]
    public void A_discarded_file_is_removed()
    {
        var path = Path.Combine(_directory, "first-admin.txt");

        AdminCredentialsFile.CreateNew(path).Discard();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_generated_password_is_long_enough_and_not_repeated()
    {
        var first = AdminCredentialsFile.GeneratePassword();
        var second = AdminCredentialsFile.GeneratePassword();

        Assert.True(first.Length >= AdminBootstrapService.MinimumPasswordLength);
        Assert.NotEqual(first, second);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
