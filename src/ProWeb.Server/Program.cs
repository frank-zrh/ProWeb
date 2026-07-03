using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using ProWeb.Server;
using ProWeb.Server.Auth;
using ProWeb.Server.Config;
using ProWeb.Server.Endpoints;
using ProWeb.Server.Fetching;
using ProWeb.Server.Middleware;
using ProWeb.Server.Storage;
using ProWeb.Shared.Content;
using ProWeb.Shared.Crypto;
using ProWeb.Shared.Serialization;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/proweb-.log", rollingInterval: RollingInterval.Day));

builder.Services.Configure<ProWebOptions>(builder.Configuration.GetSection(ProWebOptions.SectionName));
var options = builder.Configuration.GetSection(ProWebOptions.SectionName).Get<ProWebOptions>() ?? new ProWebOptions();

// Refuse to boot in Production with any built-in placeholder secret, which would yield
// forgeable JWTs and predictable at-rest key protection. We match against the full set of
// values shipped in the repository (source defaults AND appsettings.json) plus a generic
// placeholder marker, so a literal drift between the two can never silently disable the gate.
if (builder.Environment.IsProduction())
{
    if (ProductionSecretGate.IsDefaultSecret(options.Jwt.SigningKey) ||
        ProductionSecretGate.IsDefaultSecret(options.Session.MasterKey))
    {
        throw new InvalidOperationException(
            "ProWeb refuses to start in Production with a default/placeholder Jwt.SigningKey or " +
            "Session.MasterKey. Override them via configuration or environment variables.");
    }
}

// Kestrel HTTPS on the configured port (self-signed dev cert used when none is supplied).
builder.WebHost.ConfigureKestrel(kestrel =>
{
    if (options.Server.UseHttps)
    {
        kestrel.ListenAnyIP(options.Server.HttpsPort, listen =>
        {
            listen.Protocols = ProWeb.Server.Config.TransportConfigurator.ResolveProtocols(options.Server);
            void ConfigureHttps(Microsoft.AspNetCore.Server.Kestrel.Https.HttpsConnectionAdapterOptions https)
            {
                https.ClientCertificateMode =
                    ProWeb.Server.Config.TransportConfigurator.ResolveClientCertificateMode(options.Server);
            }

            if (!string.IsNullOrWhiteSpace(options.Server.CertPath) && File.Exists(options.Server.CertPath))
            {
                listen.UseHttps(options.Server.CertPath, options.Server.CertPassword, ConfigureHttps);
            }
            else
            {
                // NOTE: the certificate must NOT be disposed here. Kestrel binds the listener
                // lazily during app.Run(); disposing it inside this configuration callback would
                // invalidate the handle before bind and crash startup with
                // "m_safeCertContext is an invalid handle." Let it live for the process lifetime.
                var cert = DevCertificate.Create();
                listen.UseHttps(cert, ConfigureHttps);
            }
        });
    }
    else
    {
        kestrel.ListenAnyIP(options.Server.HttpsPort);
    }
});

RegisterServices(builder.Services);

// Rate limiting: throttle the unauthenticated handshake per source IP and the proxy/close
// endpoints per session (falling back to IP), returning 429 with Retry-After when exceeded.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.OnRejected = (context, _) =>
    {
        var opts = context.HttpContext.RequestServices.GetRequiredService<ProWebOptions>();
        context.HttpContext.Response.Headers.RetryAfter = opts.RateLimit.WindowSeconds.ToString();
        return ValueTask.CompletedTask;
    };

    limiter.AddPolicy(RateLimitPolicies.Handshake, httpContext =>
    {
        var opts = httpContext.RequestServices.GetRequiredService<ProWebOptions>().RateLimit;
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return opts.Enabled
            ? FixedWindow(key, opts.HandshakePermitPerWindow, opts.WindowSeconds)
            : NoLimit(key);
    });

    limiter.AddPolicy(RateLimitPolicies.Proxy, httpContext =>
    {
        var opts = httpContext.RequestServices.GetRequiredService<ProWebOptions>().RateLimit;
        // Prefer the bearer token (≈ session) as the partition key so one abusive session cannot
        // exhaust the shared budget; fall back to the source IP.
        var auth = httpContext.Request.Headers.Authorization.ToString();
        var key = string.IsNullOrEmpty(auth)
            ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            : auth;
        return opts.Enabled
            ? FixedWindow(key, opts.ProxyPermitPerWindow, opts.WindowSeconds)
            : NoLimit(key);
    });
});

var app = builder.Build();

// Initialize the database schema.
app.Services.GetRequiredService<SqliteBootstrapper>().Initialize();

app.UseWebSockets();
app.UseRateLimiter();
app.UseMiddleware<HstsMiddleware>();
app.UseMiddleware<RequestIdMiddleware>();
app.UseMiddleware<SessionAuthMiddleware>();

app.MapProWebEndpoints();
app.MapStreamEndpoint();

app.Run();

static RateLimitPartition<string> FixedWindow(string key, int permitPerWindow, int windowSeconds) =>
    RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = Math.Max(1, permitPerWindow),
        Window = TimeSpan.FromSeconds(Math.Max(1, windowSeconds)),
        QueueLimit = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

static RateLimitPartition<string> NoLimit(string key) =>
    RateLimitPartition.GetNoLimiter(key);

static void RegisterServices(IServiceCollection services)
{
    // Expose the bound options as a resolvable singleton for constructor injection.
    services.AddSingleton(sp =>
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProWebOptions>>().Value);

    // Shared cryptography and serialization.
    services.AddSingleton<CryptoService>();
    services.AddSingleton<EnvelopeCodec>();
    services.AddSingleton(sp =>
    {
        var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProWebOptions>>().Value;
        return new ReplayGuard(opts.Session.ReplayWindowMs);
    });
    services.AddSingleton<ContentRewriter>();

    // Storage.
    services.AddSingleton(sp =>
    {
        var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProWebOptions>>().Value;
        return new SqliteConnectionFactory(opts.Storage.ConnectionString, opts.Storage.BusyTimeoutMs);
    });
    services.AddSingleton<SqliteBootstrapper>();
    services.AddSingleton<SessionRepository>();
    services.AddSingleton<CookieRepository>();
    services.AddSingleton<CacheRepository>();
    services.AddSingleton<RequestLogRepository>();

    // Auth.
    services.AddSingleton(sp =>
        new JwtService(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProWebOptions>>().Value));
    services.AddSingleton(sp =>
        new SessionKeyProtector(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProWebOptions>>().Value));

    // Fetching.
    services.AddSingleton<SessionCookieStore>();
    services.AddSingleton<HttpClientFetcher>();
    services.AddSingleton<HeadlessBrowserFetcher>();
    services.AddSingleton<IContentFetcher>(sp => sp.GetRequiredService<HttpClientFetcher>());
    services.AddSingleton<IHeadlessFetcher>(sp => sp.GetRequiredService<HeadlessBrowserFetcher>());
    services.AddSingleton<FetcherSelector>();
    services.AddSingleton<IFetchDispatcher, FetchDispatcher>();

    // Orchestration.
    services.AddSingleton<HandshakeService>();
    services.AddSingleton<ProxyService>();

    // Background maintenance (TTL purge of expired sessions and cache entries).
    services.AddHostedService<MaintenanceService>();
}

/// <summary>Exposed so integration tests can reference the entry point via WebApplicationFactory.</summary>
public partial class Program
{
}
