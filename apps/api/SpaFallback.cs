using Microsoft.Extensions.FileProviders;

public static class SpaFallback
{
    private static readonly PathString[] BackendPrefixes =
    [
        new("/api"),
        new("/health"),
        new("/openapi"),
    ];

    public static bool IsDocumentNavigation(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        if (BackendPrefixes.Any(prefix => request.Path.StartsWithSegments(prefix)) ||
            Path.HasExtension(request.Path.Value))
        {
            return false;
        }

        var acceptsHtml = request.GetTypedHeaders().Accept?.Any(mediaType =>
            mediaType.MediaType.HasValue &&
            mediaType.MediaType.Value.ToString().Equals("text/html", StringComparison.OrdinalIgnoreCase)) ?? false;
        if (!acceptsHtml)
        {
            return false;
        }

        var destination = request.Headers["Sec-Fetch-Dest"].ToString();
        return destination.Length == 0 || destination.Equals("document", StringComparison.Ordinal);
    }

    public static async Task ServeIndexAsync(
        HttpContext context,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!IsDocumentNavigation(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        IFileInfo index = environment.WebRootFileProvider.GetFileInfo("index.html");
        if (!index.Exists)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength = index.Length;
        context.Response.Headers.CacheControl = "no-cache";
        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await using var source = index.CreateReadStream();
        await source.CopyToAsync(context.Response.Body, cancellationToken);
    }
}
