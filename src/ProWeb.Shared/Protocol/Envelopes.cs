using MessagePack;

namespace ProWeb.Shared.Protocol;

/// <summary>Request envelope carried (encrypted) from client to server on /v1/proxy.</summary>
[MessagePackObject]
public sealed class RequestEnvelope
{
    [Key(0)] public string SessionId { get; set; } = string.Empty;
    [Key(1)] public string RequestId { get; set; } = string.Empty;
    [Key(2)] public long TimestampUnixMs { get; set; }
    [Key(3)] public string Method { get; set; } = "GET";
    [Key(4)] public string TargetUrl { get; set; } = string.Empty;
    [Key(5)] public Dictionary<string, string> Headers { get; set; } = new();
    [Key(6)] public byte[]? Body { get; set; }
    [Key(7)] public string ClientVersion { get; set; } = "1.0.0";
}

/// <summary>Response envelope carried (encrypted) from server back to client.</summary>
[MessagePackObject]
public sealed class ResponseEnvelope
{
    [Key(0)] public string RequestId { get; set; } = string.Empty;
    [Key(1)] public int StatusCode { get; set; }
    [Key(2)] public Dictionary<string, string> Headers { get; set; } = new();
    [Key(3)] public byte[]? Body { get; set; }
    [Key(4)] public string ContentType { get; set; } = string.Empty;
    [Key(5)] public string FinalUrl { get; set; } = string.Empty;
    [Key(6)] public long ServerElapsedMs { get; set; }
}

/// <summary>Plain (unencrypted) JSON error returned when a request cannot be processed.</summary>
public sealed class ErrorEnvelope
{
    public string RequestId { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string Error { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>A single frame on the WebSocket /v1/stream channel (carried AES-256-GCM encrypted).</summary>
[MessagePackObject]
public sealed class StreamFrame
{
    [Key(0)] public string RequestId { get; set; } = string.Empty;
    [Key(1)] public int Seq { get; set; }
    [Key(2)] public bool Fin { get; set; }
    [Key(3)] public byte[]? Payload { get; set; }
}
