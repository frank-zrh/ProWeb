using ProWeb.Server.Storage;

namespace ProWeb.Server.Auth;

/// <summary>Ambient information about the authenticated session for the current request.</summary>
public sealed class SessionContext
{
    public SessionContext(SessionRecord session, byte[] sessionKey)
    {
        Session = session;
        SessionKey = sessionKey;
    }

    public SessionRecord Session { get; }

    public byte[] SessionKey { get; }

    public string SessionId => Session.SessionId;
}
