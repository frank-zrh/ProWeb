namespace ProWeb.Client.Core;

/// <summary>
/// Holds the active session identifier so interception adapters can stamp envelopes with the real
/// session instead of a hard-coded placeholder. Thread-safe for read/write of the id.
/// </summary>
public sealed class SessionContextAccessor
{
    private volatile string _activeSessionId = string.Empty;

    /// <summary>The current session id, or empty when no session has been established.</summary>
    public string ActiveSessionId
    {
        get => _activeSessionId;
        set => _activeSessionId = value ?? string.Empty;
    }

    /// <summary>True once a session has been established.</summary>
    public bool HasSession => !string.IsNullOrEmpty(_activeSessionId);
}
