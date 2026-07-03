using System.Text;
using ProWeb.Shared.Protocol;

namespace ProWeb.Client.Core;

/// <summary>Abstraction over the encrypted proxy channel so interception logic can be unit-tested.</summary>
public interface IProxyChannel
{
    bool IsConnected { get; }

    string? SessionId { get; }

    Task HandshakeAsync(CancellationToken cancellationToken = default);

    Task<ResponseEnvelope> FetchAsync(
        string url,
        string method = "GET",
        IReadOnlyDictionary<string, string>? headers = null,
        byte[]? body = null,
        CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}

/// <summary>How an intercepted resource request must be handled.</summary>
public enum InterceptionDecision
{
    /// <summary>Local/pseudo scheme (data:, about:, blob:…) — load with the native stack.</summary>
    LoadNatively,

    /// <summary>http(s) with an established session — fetch through the encrypted channel.</summary>
    Proxy,

    /// <summary>http(s) but no session yet — must NOT direct-connect; show an error page.</summary>
    BlockNoSession,
}

/// <summary>The outcome the interception adapter should apply to the browser.</summary>
public sealed class ProxyContentResult
{
    private ProxyContentResult(InterceptionDecision decision, int statusCode, string contentType, byte[] body)
    {
        Decision = decision;
        StatusCode = statusCode;
        ContentType = contentType;
        Body = body;
    }

    public InterceptionDecision Decision { get; }

    public int StatusCode { get; }

    public string ContentType { get; }

    public byte[] Body { get; }

    /// <summary>True when the adapter must serve <see cref="Body"/> instead of letting Chromium load.</summary>
    public bool ServesContent => Decision != InterceptionDecision.LoadNatively;

    public static ProxyContentResult Native() =>
        new(InterceptionDecision.LoadNatively, 0, string.Empty, Array.Empty<byte>());

    public static ProxyContentResult Proxied(int statusCode, string contentType, byte[] body) =>
        new(InterceptionDecision.Proxy, statusCode, contentType, body);

    public static ProxyContentResult Error(int statusCode, string contentType, byte[] body) =>
        new(InterceptionDecision.BlockNoSession, statusCode, contentType, body);
}

/// <summary>
/// Decides — and executes — how each intercepted http(s) request is served: local schemes load
/// natively, proxyable requests go through the encrypted <see cref="IProxyChannel"/>, and requests
/// made before a session exists are blocked (no direct connection to the target is ever attempted).
/// This is the seam that makes the "client never direct-connects" guarantee testable.
/// </summary>
public sealed class ProxyInterceptionCoordinator
{
    private readonly IProxyChannel _channel;

    public ProxyInterceptionCoordinator(IProxyChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public InterceptionDecision Decide(string? url)
    {
        if (!RequestSchemeClassifier.IsProxyable(url))
            return InterceptionDecision.LoadNatively;
        return _channel.IsConnected ? InterceptionDecision.Proxy : InterceptionDecision.BlockNoSession;
    }

    public async Task<ProxyContentResult> HandleAsync(
        string url,
        string method = "GET",
        IReadOnlyDictionary<string, string>? headers = null,
        byte[]? body = null,
        CancellationToken cancellationToken = default)
    {
        switch (Decide(url))
        {
            case InterceptionDecision.LoadNatively:
                return ProxyContentResult.Native();

            case InterceptionDecision.BlockNoSession:
                return ProxyContentResult.Error(
                    503,
                    "text/html; charset=utf-8",
                    Encoding.UTF8.GetBytes(ErrorPageModel.Render(
                        503, "no-session", "尚未建立到安全代理服务的会话，无法加载远程内容。")));

            default:
                try
                {
                    var response = await _channel
                        .FetchAsync(url, method, headers, body, cancellationToken)
                        .ConfigureAwait(false);
                    return ProxyContentResult.Proxied(
                        response.StatusCode,
                        string.IsNullOrEmpty(response.ContentType) ? "application/octet-stream" : response.ContentType,
                        response.Body ?? Array.Empty<byte>());
                }
                catch (ProxyRequestException ex)
                {
                    return ProxyContentResult.Error(
                        ex.StatusCode,
                        "text/html; charset=utf-8",
                        Encoding.UTF8.GetBytes(ErrorPageModel.Render(
                            ex.StatusCode, ex.RequestId, "通过安全通道加载失败。")));
                }
        }
    }
}
