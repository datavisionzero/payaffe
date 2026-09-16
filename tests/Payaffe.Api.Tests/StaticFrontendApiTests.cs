using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Payaffe.Api.Tests.Payments;

namespace Payaffe.Api.Tests;

public sealed class StaticFrontendApiTests
{
    [Fact]
    public async Task Document_deep_links_use_the_SPA_without_masking_backend_or_asset_404s()
    {
        var webRoot = Directory.CreateTempSubdirectory("payaffe-static-web-");
        try
        {
            Directory.CreateDirectory(Path.Combine(webRoot.FullName, "assets"));
            await File.WriteAllTextAsync(
                Path.Combine(webRoot.FullName, "index.html"),
                "<!doctype html><title>payaffe test shell</title>");
            await File.WriteAllTextAsync(
                Path.Combine(webRoot.FullName, "assets", "app-a1b2c3.js"),
                "export {};\n");

            await using var factory = new PaymentApiFactory
            {
                StaticWebRootPath = webRoot.FullName,
            };
            using var client = factory.CreateClient();

            using var deepLink = await client.SendAsync(DocumentRequest("/admin/payments/payment-id"));
            Assert.Equal(HttpStatusCode.OK, deepLink.StatusCode);
            Assert.Equal("text/html", deepLink.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-cache", deepLink.Headers.CacheControl?.ToString());
            Assert.Contains("payaffe test shell", await deepLink.Content.ReadAsStringAsync());

            using var head = await client.SendAsync(DocumentRequest("/pay/payer-page-id", HttpMethod.Head));
            Assert.Equal(HttpStatusCode.OK, head.StatusCode);
            Assert.Empty(await head.Content.ReadAsByteArrayAsync());

            foreach (var path in new[] { "/api/missing", "/health/missing", "/assets/missing.js" })
            {
                using var missing = await client.SendAsync(DocumentRequest(path));
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            }

            using var asset = await client.GetAsync("/assets/app-a1b2c3.js");
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
            Assert.Equal("public, max-age=31536000, immutable", asset.Headers.CacheControl?.ToString());

            using var index = await client.GetAsync("/index.html");
            Assert.Equal(HttpStatusCode.OK, index.StatusCode);
            Assert.Equal("no-cache", index.Headers.CacheControl?.ToString());
        }
        finally
        {
            webRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void Only_HTML_document_navigation_is_eligible_for_the_SPA()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/admin/payments";
        context.Request.Headers.Accept = "text/html,application/xhtml+xml";
        context.Request.Headers["Sec-Fetch-Dest"] = "document";
        Assert.True(SpaFallback.IsDocumentNavigation(context.Request));

        context.Request.Method = HttpMethods.Post;
        Assert.False(SpaFallback.IsDocumentNavigation(context.Request));
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Accept = "application/json";
        Assert.False(SpaFallback.IsDocumentNavigation(context.Request));
        context.Request.Headers.Accept = "text/html";
        context.Request.Path = "/api/admin/session";
        Assert.False(SpaFallback.IsDocumentNavigation(context.Request));
    }

    private static HttpRequestMessage DocumentRequest(string path, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Add("Sec-Fetch-Dest", "document");
        return request;
    }
}
