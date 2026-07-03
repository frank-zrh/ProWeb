namespace ProWeb.Server.Config;

/// <summary>
/// Detects secrets that ship with the repository (or that are obvious placeholders) so the server
/// can refuse to start in Production with a predictable, publicly-visible key. Centralizing the
/// known-default set eliminates the class of bug where a source constant and appsettings.json drift
/// apart and silently disable the boot gate.
/// </summary>
public static class ProductionSecretGate
{
    /// <summary>The complete set of default/placeholder secrets distributed with the repo.</summary>
    public static readonly IReadOnlySet<string> KnownDefaults = new HashSet<string>(StringComparer.Ordinal)
    {
        // JwtOptions.SigningKey source default.
        "dev-signing-key-change-me-please-32b!!",
        // appsettings.json Jwt.SigningKey (historically drifted from the source default).
        "dev-signing-key-change-me-please-32bytes!!",
        // SessionOptions.MasterKey source default == appsettings.json value.
        "5rC6r0m7t2Y0m0oQ0m9nZ0aX0bC0dE0fG0hI0jK0lM=",
    };

    /// <summary>
    /// Returns true when <paramref name="value"/> is a known shipped default, empty, or contains a
    /// generic placeholder marker (e.g. "change-me").
    /// </summary>
    public static bool IsDefaultSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (KnownDefaults.Contains(value))
            return true;
        return value.Contains("change-me", StringComparison.OrdinalIgnoreCase);
    }
}
