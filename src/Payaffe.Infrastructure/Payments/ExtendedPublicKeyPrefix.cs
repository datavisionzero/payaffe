using NBitcoin;
using NBitcoin.DataEncoders;

namespace Payaffe.Infrastructure.Payments;

/// <summary>
/// The four version bytes an extended key is serialised behind. They say which
/// currency and which wallet convention the export came from and carry no key
/// material, so the same account node re-encodes from one prefix to another
/// without touching what it derives. A wallet that exports a `zpub` and a
/// wallet that exports the `xpub` of the same node hand over the same key.
/// </summary>
internal static class ExtendedPublicKeyPrefix
{
    /// <summary>
    /// The prefixes an operator is realistically handed. NBitcoin decides what
    /// it takes; this list exists only so a rejection can name what was given
    /// and what the same key looks like in a form that is taken.
    /// </summary>
    private static readonly (string Name, uint Version, bool Spending)[] Known =
    [
        ("xpub", 0x0488B21E, false),
        ("xprv", 0x0488ADE4, true),
        ("ypub", 0x049D7CB2, false),
        ("yprv", 0x049D7878, true),
        ("zpub", 0x04B24746, false),
        ("zprv", 0x04B2430C, true),
        ("tpub", 0x043587CF, false),
        ("tprv", 0x04358394, true),
        ("upub", 0x044A5262, false),
        ("uprv", 0x044A4E28, true),
        ("vpub", 0x045F1CF6, false),
        ("vprv", 0x045F18BC, true),
        ("Ltub", 0x019DA462, false),
        ("Ltpv", 0x019D9CFE, true),
        ("Mtub", 0x01B26EF6, false),
        ("Mtpv", 0x01B26792, true),
        ("ttub", 0x0436F6E1, false),
        ("ttpv", 0x0436EF7D, true),
    ];

    /// <summary>
    /// The prefix <paramref name="value"/> is serialised behind, or null when it
    /// is not base58check at all or carries version bytes nothing here knows.
    /// </summary>
    internal static (string Name, bool Spending)? Identify(string value)
    {
        var body = Decode(value);
        if (body is null)
        {
            return null;
        }

        var version = ReadVersion(body);
        foreach (var (name, known, spending) in Known)
        {
            if (known == version)
            {
                return (name, spending);
            }
        }

        return null;
    }

    /// <summary>
    /// The prefixes <paramref name="network"/> would take for the key in
    /// <paramref name="value"/>, in the order listed above. The answer comes
    /// from re-encoding the key behind each known prefix and asking NBitcoin,
    /// rather than from a second table that could drift away from it.
    /// </summary>
    internal static IReadOnlyList<string> AcceptedFor(string value, Network network)
    {
        var body = Decode(value);
        if (body is null)
        {
            return [];
        }

        var accepted = new List<string>();
        foreach (var (name, version, spending) in Known)
        {
            if (spending)
            {
                continue;
            }

            var candidate = (byte[])body.Clone();
            candidate[0] = (byte)(version >> 24);
            candidate[1] = (byte)(version >> 16);
            candidate[2] = (byte)(version >> 8);
            candidate[3] = (byte)version;

            try
            {
                _ = ExtPubKey.Parse(Encoders.Base58Check.EncodeData(candidate), network);
                accepted.Add(name);
            }
            catch (FormatException)
            {
            }
        }

        return accepted;
    }

    private static byte[]? Decode(string value)
    {
        try
        {
            var body = Encoders.Base58Check.DecodeData(value);
            return body.Length < 4 ? null : body;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static uint ReadVersion(byte[] body) =>
        ((uint)body[0] << 24) | ((uint)body[1] << 16) | ((uint)body[2] << 8) | body[3];
}
