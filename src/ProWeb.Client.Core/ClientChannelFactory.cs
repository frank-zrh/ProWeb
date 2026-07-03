using System.Net.Http;

namespace ProWeb.Client.Core;

/// <summary>
/// Builds the HTTP transport and encrypted proxy channel from the current <see cref="ClientSettings"/>.
/// Extracted from the window so that "saving a new endpoint rebuilds the channel against the new
/// base address" (UT-X-R3-004) is unit-testable without a running server or WPF.
/// </summary>
public static class ClientChannelFactory
{
    /// <summary>Creates an <see cref="HttpClient"/> pointed at the configured server base URL.</summary>
    public static HttpClient CreateHttpClient(ClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var handler = new HttpClientHandler
        {
            // Dev deployments use a self-signed server certificate.
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        return new HttpClient(handler) { BaseAddress = new Uri(settings.ServerBaseUrl) };
    }

    /// <summary>The device id to present, falling back to the machine name when unset.</summary>
    public static string ResolveDeviceId(ClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return string.IsNullOrWhiteSpace(settings.DeviceId) ? Environment.MachineName : settings.DeviceId;
    }

    /// <summary>Creates a fresh, unconnected channel bound to the settings' base address.</summary>
    public static ClientProxyChannel CreateChannel(ClientSettings settings, out HttpClient http)
    {
        http = CreateHttpClient(settings);
        return new ClientProxyChannel(http, ResolveDeviceId(settings));
    }
}
