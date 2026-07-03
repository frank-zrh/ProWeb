using System.Reflection;

namespace ProWeb.Client.Core;

/// <summary>
/// Product identity shown in the "About ProWeb" dialog (UT-X-R3-001): name, version and build date.
/// Kept in Core (independent of WPF) so the About content is unit-testable. Version/build date are
/// read from the executing assembly with safe fallbacks.
/// </summary>
public static class AboutInfo
{
    public const string ProductName = "ProWeb";

    public const string Tagline = "安全云端浏览器 — 所有请求经加密代理转发";

    /// <summary>Assembly informational/file version, e.g. "1.0.0".</summary>
    public static string Version { get; } = ResolveVersion(typeof(AboutInfo).Assembly);

    /// <summary>Build date (yyyy-MM-dd) derived from the assembly file, or "unknown".</summary>
    public static string BuildDate { get; } = ResolveBuildDate(typeof(AboutInfo).Assembly);

    /// <summary>One-line human-readable summary for the About dialog / feedback body.</summary>
    public static string Describe() =>
        $"{ProductName} v{Version}（构建日期 {BuildDate}）";

    internal static string ResolveVersion(Assembly assembly)
    {
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // Strip any "+<git-sha>" build metadata for a clean display value.
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    internal static string ResolveBuildDate(Assembly assembly)
    {
        try
        {
            var location = assembly.Location;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
                return File.GetLastWriteTime(location).ToString("yyyy-MM-dd");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall through to unknown.
        }

        return "unknown";
    }
}
