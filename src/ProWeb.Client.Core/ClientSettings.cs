using System.Text.Json;

namespace ProWeb.Client.Core;

/// <summary>User-configurable client settings (backend endpoint, device id, home page) — UT-X-R1-004.</summary>
public sealed class ClientSettings
{
    public string ServerBaseUrl { get; set; } = "https://localhost:8443";

    public string DeviceId { get; set; } = string.Empty;

    public string HomePage { get; set; } = "https://example.com";

    public ClientSettings Clone() => new()
    {
        ServerBaseUrl = ServerBaseUrl,
        DeviceId = DeviceId,
        HomePage = HomePage,
    };

    /// <summary>Returns true when the base url is a well-formed absolute http(s) URL.</summary>
    public bool IsServerUrlValid =>
        Uri.TryCreate(ServerBaseUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

/// <summary>
/// Loads and persists <see cref="ClientSettings"/> as JSON. Defaults are returned when the file is
/// missing or unreadable so the settings dialog always has a usable baseline.
/// </summary>
public sealed class ClientSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public ClientSettingsStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public string Path => _path;

    /// <summary>Default location under the user's local application data.</summary>
    public static string DefaultPath()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProWeb");
        return System.IO.Path.Combine(dir, "settings.json");
    }

    public ClientSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new ClientSettings();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<ClientSettings>(json) ?? new ClientSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ClientSettings();
        }
    }

    public void Save(ClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var dir = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }
}
