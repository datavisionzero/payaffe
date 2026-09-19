namespace Payaffe.Sdk;

/// <summary>
/// The error correction level of a payment QR code. A higher level survives more damage and
/// dirt on a printed code, and costs modules: the same URI needs a larger symbol.
/// </summary>
public enum PayaffeQrErrorCorrection
{
    Low,
    Medium,
    Quartile,
    High,
}

/// <summary>
/// How a Payment Instruction is turned into a QR code. Every value here is presentation; none
/// of it can change what a wallet reads, because the payload is the instruction URI.
/// </summary>
public sealed class PayaffeQrCodeOptions
{
    /// <summary>
    /// Defaults to <see cref="PayaffeQrErrorCorrection.Medium"/>, which is what wallets are
    /// tested against and what keeps a screen-rendered payment URI inside a small symbol.
    /// </summary>
    public PayaffeQrErrorCorrection ErrorCorrection { get; set; } = PayaffeQrErrorCorrection.Medium;

    /// <summary>
    /// The light border around the symbol, in modules. The QR specification asks for four, and
    /// a scanner that cannot find the finder patterns against a busy checkout page is the usual
    /// cost of removing it.
    /// </summary>
    public int QuietZoneModules { get; set; } = 4;

    /// <summary>
    /// The colour of the dark modules. `currentColor` lets the surrounding page decide.
    /// </summary>
    public string DarkColor { get; set; } = "#000000";

    /// <summary>
    /// The background colour, or <see langword="null"/> for a transparent code. Scanners need
    /// the contrast, so a transparent code belongs only on a light surface.
    /// </summary>
    public string? LightColor { get; set; } = "#ffffff";

    /// <summary>
    /// An accessible name for the rendered symbol, in the product's own wording. Left unset the
    /// SVG is marked decorative, on the assumption that the address and amount are also on the
    /// page as text a screen reader can use.
    /// </summary>
    public string? AccessibleLabel { get; set; }

    internal RenderingOptions Validate()
    {
        if (QuietZoneModules is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(QuietZoneModules),
                QuietZoneModules,
                "The quiet zone must be between zero and thirty-two modules.");
        }

        // The colours are written into markup the integrating product serves, so they are
        // restricted to shapes that cannot close an attribute and open an element.
        EnsureColorIsSafe(DarkColor, nameof(DarkColor));
        if (LightColor is not null)
        {
            EnsureColorIsSafe(LightColor, nameof(LightColor));
        }

        return new RenderingOptions(
            ErrorCorrection,
            QuietZoneModules,
            DarkColor,
            LightColor,
            AccessibleLabel);
    }

    private static void EnsureColorIsSafe(string color, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(color, parameterName);

        bool isHex = color[0] == '#' &&
            color.Length is 4 or 5 or 7 or 9 &&
            color.Skip(1).All(char.IsAsciiHexDigit);
        bool isKeyword = color.All(char.IsAsciiLetter);

        if (!isHex && !isKeyword)
        {
            throw new ArgumentException(
                "A QR colour must be a hexadecimal value such as #000000 or a CSS colour keyword such as currentColor.",
                parameterName);
        }
    }
}

/// <summary>
/// A validated copy of the options, so nothing a rendered symbol depends on can change after it
/// was checked.
/// </summary>
internal sealed record RenderingOptions(
    PayaffeQrErrorCorrection ErrorCorrection,
    int QuietZoneModules,
    string DarkColor,
    string? LightColor,
    string? AccessibleLabel);
