namespace ProWeb.Client.Core;

/// <summary>
/// Builds the "报告问题 / 反馈" entry target (UT-X-R3-005). A lightweight mailto: link is used; when
/// launched from an error page it carries the current RequestId so users can report issues with the
/// diagnostic id already filled in. Kept in Core so the link construction is unit-testable.
/// </summary>
public static class FeedbackInfo
{
    public const string ContactEmail = "support@proweb.example";

    public const string MenuLabel = "报告问题 / 反馈";

    /// <summary>Builds a mailto: URL, embedding <paramref name="requestId"/> in the body when present.</summary>
    public static string BuildMailto(string? requestId = null)
    {
        var subject = Uri.EscapeDataString($"ProWeb 反馈（{AboutInfo.Describe()}）");
        var bodyText = string.IsNullOrWhiteSpace(requestId)
            ? "请描述你遇到的问题："
            : $"Request ID: {requestId}{Environment.NewLine}{Environment.NewLine}请描述你遇到的问题：";
        return $"mailto:{ContactEmail}?subject={subject}&body={Uri.EscapeDataString(bodyText)}";
    }
}
