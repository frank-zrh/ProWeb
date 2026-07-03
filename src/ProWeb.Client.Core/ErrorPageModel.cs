using System.Net;

namespace ProWeb.Client.Core;

/// <summary>Renders a user-facing error page (including the RequestId for diagnostics).</summary>
public static class ErrorPageModel
{
    public static string Render(int statusCode, string requestId, string? detail = null)
    {
        var title = TitleFor(statusCode);
        var safeDetail = WebUtility.HtmlEncode(detail ?? string.Empty);
        var safeRequestId = WebUtility.HtmlEncode(requestId);
        var reportLink = WebUtility.HtmlEncode(FeedbackInfo.BuildMailto(requestId));

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><title>{{statusCode}} {{title}}</title></head>
            <body style="font-family:Segoe UI,Arial,sans-serif;padding:2rem;color:#333;">
              <h1 style="color:#c0392b;">{{statusCode}} · {{title}}</h1>
              <p>The page could not be loaded through the secure channel.</p>
              {{(safeDetail.Length > 0 ? $"<p>{safeDetail}</p>" : string.Empty)}}
              <p style="color:#888;font-size:0.85rem;">Request ID: {{safeRequestId}}</p>
              <p><a href="{{reportLink}}" style="color:#1976d2;font-size:0.85rem;">报告问题 / 反馈</a></p>
            </body>
            </html>
            """;
    }

    private static string TitleFor(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Session Expired",
        403 => "Forbidden",
        409 => "Duplicate Request",
        429 => "Too Many Requests",
        502 => "Upstream Fetch Failed",
        503 => "Service Unavailable",
        _ => statusCode >= 500 ? "Server Error" : "Request Failed",
    };

    /// <summary>
    /// Renders an error page for a browser-level load failure (e.g. CefSharp <c>LoadError</c>),
    /// surfacing the failing URL, the underlying error text, a RequestId, and a retry hint
    /// (UT-F-R1-002).
    /// </summary>
    public static string RenderLoadFailure(string url, string errorCode, string? errorText, string requestId)
    {
        var safeUrl = WebUtility.HtmlEncode(url ?? string.Empty);
        var safeErr = WebUtility.HtmlEncode(errorText ?? string.Empty);
        var safeCode = WebUtility.HtmlEncode(errorCode ?? string.Empty);
        var safeRequestId = WebUtility.HtmlEncode(requestId ?? string.Empty);
        var reportLink = WebUtility.HtmlEncode(FeedbackInfo.BuildMailto(requestId));

        return $$"""
            <!DOCTYPE html>
            <html lang="zh-CN">
            <head><meta charset="utf-8"><title>无法加载页面</title></head>
            <body style="font-family:Segoe UI,Microsoft YaHei,Arial,sans-serif;padding:2rem;color:#333;">
              <h1 style="color:#c0392b;">无法加载此页面</h1>
              <p>通过安全代理通道加载 <code>{{safeUrl}}</code> 时失败。</p>
              {{(safeErr.Length > 0 ? $"<p>原因：{safeErr}（{safeCode}）</p>" : $"<p>错误代码：{safeCode}</p>")}}
              <p><button onclick="location.reload()"
                 style="padding:.5rem 1rem;border:0;border-radius:4px;background:#1976d2;color:#fff;cursor:pointer;">
                 重试</button></p>
              <p style="color:#888;font-size:0.85rem;">Request ID: {{safeRequestId}}</p>
              <p><a href="{{reportLink}}" style="color:#1976d2;font-size:0.85rem;">报告问题 / 反馈</a></p>
            </body>
            </html>
            """;
    }
}
