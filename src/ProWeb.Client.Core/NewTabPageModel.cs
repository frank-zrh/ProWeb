namespace ProWeb.Client.Core;

/// <summary>Renders the new-tab empty-state page shown when a tab has no content (UT-F-R1-004).</summary>
public static class NewTabPageModel
{
    public const string Marker = "proweb-newtab";

    public static string Render() =>
        """
        <!DOCTYPE html>
        <html lang="zh-CN" data-page="proweb-newtab">
        <head><meta charset="utf-8"><title>新标签页</title></head>
        <body style="font-family:Segoe UI,Microsoft YaHei,Arial,sans-serif;margin:0;
                     display:flex;flex-direction:column;align-items:center;justify-content:center;
                     height:100vh;color:#37474f;background:#fafafa;">
          <div style="font-size:2rem;margin-bottom:.5rem;">🔒 ProWeb</div>
          <p style="color:#607d8b;">安全云端浏览器 — 所有请求经加密代理转发</p>
          <p style="color:#90a4ae;font-size:.9rem;">在上方地址栏输入网址或搜索词开始浏览</p>
        </body>
        </html>
        """;
}
