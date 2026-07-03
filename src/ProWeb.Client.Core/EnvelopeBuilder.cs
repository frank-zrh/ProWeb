using ProWeb.Shared.Protocol;

namespace ProWeb.Client.Core;

/// <summary>Builds <see cref="RequestEnvelope"/> instances from intercepted resource requests.</summary>
public sealed class EnvelopeBuilder
{
    private readonly Func<long> _now;
    private readonly Func<string> _idFactory;

    public EnvelopeBuilder(Func<long>? nowProvider = null, Func<string>? idFactory = null)
    {
        _now = nowProvider ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _idFactory = idFactory ?? (() => Guid.NewGuid().ToString("N"));
    }

    public RequestEnvelope Build(
        string sessionId,
        string method,
        string targetUrl,
        IReadOnlyDictionary<string, string>? headers = null,
        byte[]? body = null,
        string clientVersion = "1.0.0")
    {
        var envelope = new RequestEnvelope
        {
            SessionId = sessionId,
            RequestId = _idFactory(),
            TimestampUnixMs = _now(),
            Method = string.IsNullOrWhiteSpace(method) ? "GET" : method.ToUpperInvariant(),
            TargetUrl = targetUrl,
            Body = body,
            ClientVersion = clientVersion,
        };

        if (headers is not null)
        {
            foreach (var (key, value) in headers)
                envelope.Headers[key] = value;
        }

        return envelope;
    }
}
