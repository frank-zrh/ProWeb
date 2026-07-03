namespace ProWeb.Client.Core;

/// <summary>Observed state of the encrypted proxy channel.</summary>
public enum SecureChannelState
{
    Connecting,
    Secure,
    Disconnected,
}

/// <summary>
/// Drives the security badge so it reflects the REAL session state instead of a static label
/// (UT-X-R1-003). Provides accessible, non-jargon text plus a tooltip.
/// </summary>
public sealed class SecurityStatusModel
{
    private SecurityStatusModel(SecureChannelState state, string text, string tooltip, bool isSecure)
    {
        State = state;
        Text = text;
        Tooltip = tooltip;
        IsSecure = isSecure;
    }

    public SecureChannelState State { get; }

    public string Text { get; }

    public string Tooltip { get; }

    public bool IsSecure { get; }

    public static SecurityStatusModel For(SecureChannelState state) => state switch
    {
        SecureChannelState.Secure => new SecurityStatusModel(
            state, "🔒 加密通道",
            "已通过端到端加密的云端代理连接；本机不直接访问目标站点。", true),
        SecureChannelState.Connecting => new SecurityStatusModel(
            state, "… 正在连接",
            "正在与安全代理服务建立加密会话。", false),
        _ => new SecurityStatusModel(
            state, "⚠ 未连接",
            "尚未建立安全会话，远程内容将无法加载。", false),
    };

    /// <summary>Maps a channel's connection flag to a badge state.</summary>
    public static SecurityStatusModel From(IProxyChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return For(channel.IsConnected ? SecureChannelState.Secure : SecureChannelState.Disconnected);
    }
}
