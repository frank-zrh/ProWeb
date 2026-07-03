using CefSharp;
using CefSharp.Handler;
using ProWeb.Client.Core;

namespace ProWeb.Client;

/// <summary>
/// CefSharp request handler that routes every proxyable (http/https) resource request through the
/// encrypted <see cref="ProxyInterceptionCoordinator"/> instead of Chromium's default network
/// stack. Local/pseudo schemes continue to load natively. This makes the product's core promise —
/// "the client never direct-connects to target sites" — real at runtime; the decision/serving logic
/// itself lives in (and is unit-tested through) ProWeb.Client.Core.
/// </summary>
public sealed class SecureRequestHandler : RequestHandler
{
    private readonly ProxyInterceptionCoordinator _coordinator;

    public SecureRequestHandler(ProxyInterceptionCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    protected override IResourceRequestHandler? GetResourceRequestHandler(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IRequest request,
        bool isNavigation,
        bool isDownload,
        string requestInitiator,
        ref bool disableDefaultHandling)
    {
        // Local/pseudo schemes must load natively and are never proxied.
        if (!RequestSchemeClassifier.IsProxyable(request.Url))
            return null;

        // Take over handling: Chromium must not open its own connection to the target.
        disableDefaultHandling = true;
        return new SecureResourceRequestHandler(_coordinator);
    }
}

/// <summary>Serves each proxyable resource with content fetched over the encrypted channel.</summary>
public sealed class SecureResourceRequestHandler : ResourceRequestHandler
{
    private readonly ProxyInterceptionCoordinator _coordinator;

    public SecureResourceRequestHandler(ProxyInterceptionCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    protected override IResourceHandler? GetResourceHandler(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IRequest request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in request.Headers.AllKeys)
        {
            if (key is not null)
                headers[key] = request.Headers[key] ?? string.Empty;
        }

        // Carry the request body for write methods (POST/PUT/PATCH/DELETE) so form posts and
        // XHR/Fetch payloads reach the target through the proxy instead of arriving empty
        // (UT-F-R3-001). Each CEF PostDataElement's bytes are concatenated in order.
        byte[]? body = null;
        if (PostDataReader.MethodCanHaveBody(request.Method) && request.PostData is { } postData)
        {
            body = PostDataReader.Combine(postData.Elements.Select(
                e => e.Type == PostDataElementType.Bytes ? e.Bytes : null));
        }

        // The CEF IO thread requires a synchronous handler here; block on the async channel call.
        // All actual networking happens inside the encrypted channel, never directly to the target.
        var result = _coordinator
            .HandleAsync(request.Url, request.Method, headers, body)
            .GetAwaiter()
            .GetResult();

        if (!result.ServesContent)
            return null;

        var mime = result.ContentType.Split(';', 2)[0].Trim();
        if (string.IsNullOrEmpty(mime))
            mime = "application/octet-stream";

        if (result.Body == null || result.Body.Length == 0)
            return null; // Let Chromium handle it or return an error handler

        return CefSharp.ResourceHandler.FromByteArray(result.Body, mime);
    }
}
