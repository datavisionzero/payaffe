namespace Payaffe.Application.Installation;

/// <summary>
/// Whether this installation handles real payments or simulated ones
/// (ADR 0033).
/// </summary>
public enum InstallationMode
{
    Live,
    Test,
}

/// <summary>
/// The Installation Mode this process was configured with.
/// </summary>
/// <remarks>
/// A host compares it with the mode recorded in the database before it
/// serves anything, so code that branches on it is looking at the mode the
/// database agrees with.
/// </remarks>
public sealed record ConfiguredInstallationMode(InstallationMode Mode)
{
    public const string ConfigurationKey = "Installation:Mode";

    public static ConfiguredInstallationMode Live { get; } = new(InstallationMode.Live);

    public bool IsTest => Mode == InstallationMode.Test;

    /// <summary>The value stored in the database and written in configuration.</summary>
    public string Name => ToName(Mode);

    public static string ToName(InstallationMode mode) => mode switch
    {
        InstallationMode.Live => "live",
        InstallationMode.Test => "test",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static bool TryParseName(string? value, out InstallationMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "live":
                mode = InstallationMode.Live;
                return true;
            case "test":
                mode = InstallationMode.Test;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    /// <summary>
    /// Reads the configured value. Absent means <c>live</c>: an installation
    /// is never in Test Mode without somebody having said so.
    /// </summary>
    public static ConfiguredInstallationMode FromConfiguration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Live;
        }

        return TryParseName(value, out var mode)
            ? new ConfiguredInstallationMode(mode)
            : throw new InvalidOperationException(
                $"{ConfigurationKey} must be 'live' or 'test'; '{value}' is neither.");
    }
}
