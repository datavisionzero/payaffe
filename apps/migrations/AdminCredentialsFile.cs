using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Payaffe.Migrations;

/// <summary>
/// Where <c>bootstrap-admin --credentials-file</c> leaves the first Admin
/// Account's password and Recovery Codes, so that nothing secret has to pass
/// through a terminal the command runs in (ADR 0037).
/// </summary>
/// <remarks>
/// The file is created before the account is, exclusively and readable by its
/// owner only, so that a path that cannot be written, or already holds a file,
/// stops the command while nothing exists yet. An account whose only copy of
/// its password went nowhere is the one outcome this must never produce.
/// </remarks>
public sealed class AdminCredentialsFile : IDisposable
{
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private const int GeneratedPasswordLength = 32;

    private readonly FileStream _stream;
    private bool _written;

    private AdminCredentialsFile(string path, FileStream stream)
    {
        Path = path;
        _stream = stream;
    }

    public string Path { get; }

    /// <summary>
    /// Creates the file, refusing one that already exists.
    /// </summary>
    /// <exception cref="IOException">The file exists or cannot be created.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory cannot be written.</exception>
    public static AdminCredentialsFile CreateNew(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new AdminCredentialsFile(path, new FileStream(path, options));
    }

    /// <summary>
    /// A password nobody chose: 32 characters from an alphabet without the
    /// ones that are misread when copied by hand, about 186 bits.
    /// </summary>
    public static string GeneratePassword() =>
        RandomNumberGenerator.GetString(PasswordAlphabet, GeneratedPasswordLength);

    public void Write(
        Guid adminAccountId,
        string username,
        string password,
        IReadOnlyList<string> recoveryCodes,
        DateTimeOffset createdAt)
    {
        var content = new StringBuilder()
            .AppendLine("payaffe: first Admin Account")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Created:          {createdAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}")
            .AppendLine(CultureInfo.InvariantCulture, $"Admin Account id: {adminAccountId:D}")
            .AppendLine(CultureInfo.InvariantCulture, $"Username:         {username}")
            .AppendLine(CultureInfo.InvariantCulture, $"Password:         {password}")
            .AppendLine()
            .AppendLine("Recovery Codes, each usable once, for when the password is lost:");
        foreach (var recoveryCode in recoveryCodes)
        {
            content.AppendLine(CultureInfo.InvariantCulture, $"  {recoveryCode}");
        }

        content
            .AppendLine()
            .AppendLine("Sign in at /admin on this installation with the username and password.")
            .AppendLine("Then move the password and the Recovery Codes into a password manager")
            .AppendLine("and delete this file. It is the only copy: payaffe keeps neither in a")
            .AppendLine("form that can be read back.");

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content.ToString());
        _stream.Write(bytes);
        _stream.Flush(flushToDisk: true);
        _written = true;
    }

    /// <summary>
    /// Removes the file when no account was created, so a failed run leaves
    /// nothing behind that a second run would refuse to overwrite.
    /// </summary>
    public void Discard()
    {
        _stream.Dispose();
        if (!_written)
        {
            File.Delete(Path);
        }
    }

    public void Dispose() => _stream.Dispose();
}
