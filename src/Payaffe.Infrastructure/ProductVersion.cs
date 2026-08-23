using System.Reflection;

namespace Payaffe.Infrastructure;

/// <summary>
/// The released product version, as stamped by `Directory.Build.props` and
/// pointed at by the matching Git tag.
/// </summary>
public static class ProductVersion
{
    /// <summary>
    /// The version without the build metadata that source-link appends, for
    /// example <c>0.1.0</c> rather than <c>0.1.0+abc123</c>.
    /// </summary>
    public static string Value { get; } = Resolve();

    private static string Resolve()
    {
        var informationalVersion = typeof(ProductVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "0.0.0";
        }

        // Source-link builds append `+<commit>`, which belongs in neither an
        // outbound header nor a telemetry resource attribute.
        var separatorIndex = informationalVersion.IndexOf('+');
        return separatorIndex < 0 ? informationalVersion : informationalVersion[..separatorIndex];
    }
}
