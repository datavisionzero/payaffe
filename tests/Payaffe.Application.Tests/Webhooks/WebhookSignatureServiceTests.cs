using Payaffe.Application.Webhooks;

namespace Payaffe.Application.Tests.Webhooks;

public sealed class WebhookSignatureServiceTests
{
    [Fact]
    public void CreateSignature_uses_timestamp_dot_raw_body_hmac_sha256()
    {
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        const string rawBody = "{\"event_id\":\"evt_123\"}";

        var signature = WebhookSignatureService.CreateSignature("top-secret", timestamp, rawBody);

        Assert.Equal(
            "v1=3049bb7a209c34e9eb113140900431b87add41bb08e9f2764df13016623cb502",
            signature);
    }
}
