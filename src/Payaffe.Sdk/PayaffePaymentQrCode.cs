using System.Globalization;
using System.Text;
using Net.Codecrete.QrCodeGenerator;

namespace Payaffe.Sdk;

/// <summary>
/// A QR code carrying one Payment Instruction, encoded in this process and rendered as markup
/// the integrating product serves from its own origin. No request leaves the application to
/// produce it, and nothing about the Payment is recomputed: the payload is the
/// <see cref="PaymentInstruction.Uri"/> exactly as the Integration API returned it.
/// </summary>
public sealed class PayaffePaymentQrCode
{
    private readonly QrCode _qrCode;

    private readonly RenderingOptions _options;

    private PayaffePaymentQrCode(QrCode qrCode, string payload, RenderingOptions options)
    {
        _qrCode = qrCode;
        Payload = payload;
        _options = options;
    }

    /// <summary>
    /// The encoded payload, byte for byte the Payment Instruction URI. A wallet that scans the
    /// code reads this string; anything the product wants to show next to the code, such as the
    /// amount or the address, comes from the same instruction rather than from here.
    /// </summary>
    public string Payload { get; }

    /// <summary>
    /// The width and height of the symbol in modules, excluding the quiet zone.
    /// </summary>
    public int ModuleCount => _qrCode.Size;

    /// <summary>
    /// The QR version the payload required, between 1 and 40. Useful when a layout has to
    /// reserve space before the Payment exists.
    /// </summary>
    public int Version => _qrCode.Version;

    /// <summary>
    /// The light border this code is rendered with, in modules. A caller rendering the module
    /// matrix itself has to keep the same border, or a scanner may not find the symbol.
    /// </summary>
    public int QuietZoneModules => _options.QuietZoneModules;

    /// <summary>
    /// Encodes the Payment Instruction of a Payment whose Currency Selection has been made.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The instruction carries no URI, which means it did not come from a Currency Selection.
    /// </exception>
    public static PayaffePaymentQrCode Create(
        PaymentInstruction instruction,
        PayaffeQrCodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        return CreateForUri(instruction.Uri, options);
    }

    /// <summary>
    /// Encodes a Payment Instruction URI that the product already holds, for a caller that
    /// stored the URI rather than the whole instruction. The URI is encoded unchanged: building
    /// one from an address and an amount is the Integration API's job, because only it knows the
    /// Rate Lock, the network, and the exact precision of the expected amount.
    /// </summary>
    public static PayaffePaymentQrCode CreateForUri(
        string paymentUri,
        PayaffeQrCodeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentUri);

        // Validated and then copied, in that order. Holding on to the caller's instance would
        // mean a colour that passed validation could be swapped for one that never did, after
        // the check and before the markup is written.
        RenderingOptions effectiveOptions = (options ?? new PayaffeQrCodeOptions()).Validate();

        QrCode qrCode = QrCode.EncodeText(paymentUri, ToEcc(effectiveOptions.ErrorCorrection));
        return new PayaffePaymentQrCode(qrCode, paymentUri, effectiveOptions);
    }

    /// <summary>
    /// Whether the module at the given position is dark. Coordinates outside the symbol belong
    /// to the quiet zone and are light, so a caller rendering with a border does not have to
    /// special-case its edges.
    /// </summary>
    public bool IsDark(int x, int y) => _qrCode.GetModule(x, y);

    /// <summary>
    /// The symbol as a matrix indexed by row and then column, without the quiet zone, for a
    /// product that renders through its own imaging stack rather than the SVG below.
    /// </summary>
    public bool[,] ToModuleMatrix()
    {
        bool[,] modules = new bool[ModuleCount, ModuleCount];
        for (int y = 0; y < ModuleCount; y++)
        {
            for (int x = 0; x < ModuleCount; x++)
            {
                modules[y, x] = _qrCode.GetModule(x, y);
            }
        }

        return modules;
    }

    /// <summary>
    /// The symbol as a standalone SVG document, sized in modules through its `viewBox` so the
    /// surrounding page decides the displayed size with CSS. It references no font, no external
    /// image, and no Payaffe origin, so the product can inline it or serve it as a file of its
    /// own.
    /// </summary>
    public string ToSvg()
    {
        int quietZone = _options.QuietZoneModules;
        int extent = ModuleCount + (quietZone * 2);
        StringBuilder svg = new();

        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {extent} {extent}\" shape-rendering=\"crispEdges\"");
        svg.Append(_options.AccessibleLabel is null ? " aria-hidden=\"true\">" : " role=\"img\">");

        if (_options.AccessibleLabel is not null)
        {
            svg.Append("<title>").Append(EscapeXmlText(_options.AccessibleLabel)).Append("</title>");
        }

        if (_options.LightColor is not null)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<rect width=\"{extent}\" height=\"{extent}\" fill=\"{_options.LightColor}\"/>");
        }

        svg.Append("<path fill=\"").Append(_options.DarkColor).Append("\" d=\"");
        for (int y = 0; y < ModuleCount; y++)
        {
            // Consecutive dark modules become one horizontal run, which keeps the path short
            // enough to inline in a page without the document growing by tens of kilobytes.
            int x = 0;
            while (x < ModuleCount)
            {
                if (!_qrCode.GetModule(x, y))
                {
                    x++;
                    continue;
                }

                int runStart = x;
                while (x < ModuleCount && _qrCode.GetModule(x, y))
                {
                    x++;
                }

                svg.Append(CultureInfo.InvariantCulture, $"M{runStart + quietZone} {y + quietZone}h{x - runStart}v1h-{x - runStart}z");
            }
        }

        svg.Append("\"/></svg>");
        return svg.ToString();
    }

    private static QrCode.Ecc ToEcc(PayaffeQrErrorCorrection errorCorrection) => errorCorrection switch
    {
        PayaffeQrErrorCorrection.Low => QrCode.Ecc.Low,
        PayaffeQrErrorCorrection.Medium => QrCode.Ecc.Medium,
        PayaffeQrErrorCorrection.Quartile => QrCode.Ecc.Quartile,
        PayaffeQrErrorCorrection.High => QrCode.Ecc.High,
        _ => throw new ArgumentOutOfRangeException(
            nameof(errorCorrection),
            errorCorrection,
            "Unknown QR error correction level."),
    };

    private static string EscapeXmlText(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
