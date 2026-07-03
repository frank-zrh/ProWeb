using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace ProWeb.Server.Config;

/// <summary>
/// Maps <see cref="ServerOptions"/> onto concrete Kestrel transport settings. Extracted as a pure
/// helper so the protocol/mTLS/HSTS wiring is unit-testable without booting Kestrel.
/// </summary>
public static class TransportConfigurator
{
    /// <summary>Resolves the enabled HTTP protocols based on the HTTP/3 toggle.</summary>
    public static HttpProtocols ResolveProtocols(ServerOptions options) =>
        options.EnableHttp3
            ? HttpProtocols.Http1AndHttp2AndHttp3
            : HttpProtocols.Http1AndHttp2;

    /// <summary>Resolves the client-certificate negotiation mode for mutual TLS.</summary>
    public static ClientCertificateMode ResolveClientCertificateMode(ServerOptions options) =>
        options.RequireClientCertificate
            ? ClientCertificateMode.RequireCertificate
            : ClientCertificateMode.NoCertificate;

    /// <summary>Builds the HSTS header value, or null when HSTS is disabled.</summary>
    public static string? BuildHstsHeaderValue(ServerOptions options) =>
        options.EnableHsts
            ? $"max-age={options.HstsMaxAgeSeconds}; includeSubDomains"
            : null;
}
