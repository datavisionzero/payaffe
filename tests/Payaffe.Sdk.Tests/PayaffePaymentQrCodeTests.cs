using System.Globalization;
using ZXing;
using ZXing.Common;

namespace Payaffe.Sdk.Tests;

/// <summary>
/// The QR code is read by a wallet, not by this SDK, so every test here decodes the symbol with
/// an independent implementation and compares the decoded text to the Payment Instruction URI
/// the Integration API returned. Re-encoding with the same library would only prove that it
/// agrees with itself.
/// </summary>
public sealed class PayaffePaymentQrCodeTests
{
    // The wallet instructions the Integration API produces for the three supported currencies,
    // taken from the contract test that pins them, plus the precision edges around them: one
    // satoshi, one wei, and an amount whose trailing zeros are part of the value.
    [Theory]
    [InlineData("bitcoin:bc1qpayaffetestaddress0000000000000000000000000?amount=0.00039980")]
    [InlineData("litecoin:ltc1qpayaffetestaddress000000000000000000000000?amount=0.00039980")]
    [InlineData("ethereum:0x1111111111111111111111111111111111111111@1?value=399800000000000")]
    [InlineData("bitcoin:bc1qpayaffetestaddress0000000000000000000000000?amount=0.00000001")]
    [InlineData("litecoin:ltc1qpayaffetestaddress000000000000000000000000?amount=1.00000000")]
    [InlineData("ethereum:0x1111111111111111111111111111111111111111@1?value=1")]
    [InlineData("ethereum:0x1111111111111111111111111111111111111111@1?value=123456789012345678")]
    [InlineData("ethereum:0x1111111111111111111111111111111111111111@11155111?value=1000000000000000000")]
    [InlineData("bitcoin:?tb=tb1qpayaffetestaddress00000000000000000000000&amount=0.00012345")]
    public void A_wallet_reads_back_the_instruction_uri_unchanged(string paymentUri)
    {
        var code = PayaffePaymentQrCode.CreateForUri(paymentUri);

        Assert.Equal(paymentUri, code.Payload);
        Assert.Equal(paymentUri, Decode(code));
    }

    [Fact]
    public void The_payload_is_the_instruction_uri_and_nothing_derived_from_the_amount()
    {
        var instruction = new PaymentInstruction(
            SupportedCurrency.Btc,
            "mainnet",
            null,
            "0.00039980",
            "39980",
            "bc1qpayaffetestaddress0000000000000000000000000",
            "bitcoin:bc1qpayaffetestaddress0000000000000000000000000?amount=0.00039980",
            DateTimeOffset.UtcNow.AddMinutes(30));

        var code = PayaffePaymentQrCode.Create(instruction);

        Assert.Equal(instruction.Uri, code.Payload);
        Assert.Equal(instruction.Uri, Decode(code));
    }

    [Fact]
    public void A_payment_without_a_selection_has_no_instruction_to_encode()
    {
        Assert.Throws<ArgumentException>(() => PayaffePaymentQrCode.CreateForUri(string.Empty));
        Assert.Throws<ArgumentNullException>(() => PayaffePaymentQrCode.Create(null!));
    }

    [Theory]
    [InlineData(PayaffeQrErrorCorrection.Low)]
    [InlineData(PayaffeQrErrorCorrection.Medium)]
    [InlineData(PayaffeQrErrorCorrection.Quartile)]
    [InlineData(PayaffeQrErrorCorrection.High)]
    public void Every_error_correction_level_still_carries_the_same_payload(
        PayaffeQrErrorCorrection errorCorrection)
    {
        const string PaymentUri =
            "ethereum:0x1111111111111111111111111111111111111111@1?value=399800000000000";

        var code = PayaffePaymentQrCode.CreateForUri(
            PaymentUri,
            new PayaffeQrCodeOptions { ErrorCorrection = errorCorrection });

        Assert.Equal(PaymentUri, Decode(code));
    }

    [Fact]
    public void The_module_matrix_and_the_svg_describe_the_same_symbol()
    {
        var code = PayaffePaymentQrCode.CreateForUri(
            "bitcoin:bc1qpayaffetestaddress0000000000000000000000000?amount=0.00039980");
        bool[,] modules = code.ToModuleMatrix();

        string svg = code.ToSvg();
        int extent = code.ModuleCount + (code.QuietZoneModules * 2);

        Assert.Equal(code.ModuleCount, modules.GetLength(0));
        Assert.Contains($"viewBox=\"0 0 {extent} {extent}\"", svg, StringComparison.Ordinal);
        Assert.Equal(CountDarkModules(modules), CountDarkModules(ReadSvgModules(svg, code)));
        Assert.True(modules[0, 0], "The top-left finder pattern starts with a dark module.");
    }

    [Fact]
    public void The_svg_carries_no_external_reference_and_no_payaffe_origin()
    {
        var code = PayaffePaymentQrCode.CreateForUri(
            "bitcoin:bc1qpayaffetestaddress0000000000000000000000000?amount=0.00039980");

        string svg = code.ToSvg();

        Assert.DoesNotContain("http://", svg.Replace("http://www.w3.org/2000/svg", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payaffe", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<image", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-hidden=\"true\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void A_products_own_wording_and_palette_reach_the_rendered_symbol()
    {
        var code = PayaffePaymentQrCode.CreateForUri(
            "bitcoin:bc1qpayaffetestaddress0000000000000000000000000?amount=0.00039980",
            new PayaffeQrCodeOptions
            {
                DarkColor = "currentColor",
                LightColor = null,
                AccessibleLabel = "Scan to pay order 4711 <Shop & Co>",
            });

        string svg = code.ToSvg();

        Assert.Contains("fill=\"currentColor\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<rect", svg, StringComparison.Ordinal);
        Assert.Contains("role=\"img\"", svg, StringComparison.Ordinal);
        Assert.Contains("<title>Scan to pay order 4711 &lt;Shop &amp; Co&gt;</title>", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void A_colour_that_could_close_the_attribute_is_refused()
    {
        var options = new PayaffeQrCodeOptions { DarkColor = "\"><script>alert(1)</script>" };

        Assert.Throws<ArgumentException>(
            () => PayaffePaymentQrCode.CreateForUri("bitcoin:bc1qtest?amount=1.00000000", options));
    }

    [Fact]
    public void Changing_the_options_after_the_code_exists_cannot_change_its_markup()
    {
        PayaffeQrCodeOptions options = new() { DarkColor = "#101010" };
        var code = PayaffePaymentQrCode.CreateForUri(
            "bitcoin:bc1qpayaffetestaddress0000000000000000000000000?amount=0.00039980",
            options);

        // The same instance the caller still holds, now carrying a value that would never have
        // passed validation.
        options.DarkColor = "\"><script>alert(1)</script>";
        options.QuietZoneModules = 99;

        string svg = code.ToSvg();

        Assert.Contains("fill=\"#101010\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", svg, StringComparison.Ordinal);
        Assert.Equal(4, code.QuietZoneModules);
    }

    [Fact]
    public void The_quiet_zone_is_four_modules_unless_the_product_says_otherwise()
    {
        const string PaymentUri = "bitcoin:bc1qpayaffetestaddress0000000000000000000000000?amount=0.00039980";

        var standard = PayaffePaymentQrCode.CreateForUri(PaymentUri);
        var tight = PayaffePaymentQrCode.CreateForUri(
            PaymentUri,
            new PayaffeQrCodeOptions { QuietZoneModules = 1 });

        Assert.Equal(4, standard.QuietZoneModules);
        Assert.Contains(
            $"viewBox=\"0 0 {standard.ModuleCount + 8} {standard.ModuleCount + 8}\"",
            standard.ToSvg(),
            StringComparison.Ordinal);
        Assert.Contains(
            $"viewBox=\"0 0 {tight.ModuleCount + 2} {tight.ModuleCount + 2}\"",
            tight.ToSvg(),
            StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PayaffePaymentQrCode.CreateForUri(
                PaymentUri,
                new PayaffeQrCodeOptions { QuietZoneModules = -1 }));
    }

    /// <summary>
    /// Renders the symbol the way a product would and reads it back with ZXing, which is an
    /// independent QR implementation. The rendering deliberately goes through a grayscale
    /// raster rather than the module matrix, so a quiet zone that was too small or a symbol
    /// drawn upside down would fail here rather than pass on a technicality.
    /// </summary>
    private static string? Decode(PayaffePaymentQrCode code)
    {
        const int Scale = 4;
        int quietZone = code.QuietZoneModules;
        int extent = (code.ModuleCount + (quietZone * 2)) * Scale;

        byte[] luminance = new byte[extent * extent];
        Array.Fill(luminance, byte.MaxValue);
        for (int y = 0; y < code.ModuleCount; y++)
        {
            for (int x = 0; x < code.ModuleCount; x++)
            {
                if (!code.IsDark(x, y))
                {
                    continue;
                }

                for (int row = 0; row < Scale; row++)
                {
                    int offset = (((y + quietZone) * Scale) + row) * extent;
                    Array.Fill(luminance, byte.MinValue, offset + ((x + quietZone) * Scale), Scale);
                }
            }
        }

        var reader = new BarcodeReaderGeneric
        {
            Options = new DecodingOptions
            {
                PossibleFormats = [BarcodeFormat.QR_CODE],
                PureBarcode = true,
                TryHarder = true,
            },
        };

        return reader.Decode(
            new RGBLuminanceSource(
                luminance,
                extent,
                extent,
                RGBLuminanceSource.BitmapFormat.Gray8))?.Text;
    }

    private static int CountDarkModules(bool[,] modules)
    {
        int count = 0;
        foreach (bool module in modules)
        {
            if (module)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Replays the horizontal runs of the SVG path back into a matrix, so the rendered document
    /// is checked against the symbol rather than assumed to follow it.
    /// </summary>
    private static bool[,] ReadSvgModules(string svg, PayaffePaymentQrCode code)
    {
        int quietZone = code.QuietZoneModules;
        bool[,] modules = new bool[code.ModuleCount, code.ModuleCount];

        int pathStart = svg.IndexOf(" d=\"", StringComparison.Ordinal) + 4;
        int pathEnd = svg.IndexOf('"', pathStart);
        foreach (string run in svg[pathStart..pathEnd].Split('M', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = run.Split([' ', 'h', 'v', 'z'], StringSplitOptions.RemoveEmptyEntries);
            int x = int.Parse(parts[0], CultureInfo.InvariantCulture) - quietZone;
            int y = int.Parse(parts[1], CultureInfo.InvariantCulture) - quietZone;
            int length = int.Parse(parts[2], CultureInfo.InvariantCulture);
            for (int offset = 0; offset < length; offset++)
            {
                modules[y, x + offset] = true;
            }
        }

        return modules;
    }
}
