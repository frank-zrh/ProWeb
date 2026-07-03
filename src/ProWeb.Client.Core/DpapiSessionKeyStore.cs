using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace ProWeb.Client.Core;

/// <summary>
/// Protects the session key at rest on the client using Windows DPAPI (current-user scope).
/// The key never leaves the machine in plaintext. On non-Windows platforms protection falls back
/// to returning the bytes unchanged (the WPF client only ships for Windows).
/// </summary>
public sealed class DpapiSessionKeyStore
{
    private static readonly byte[] Entropy =
        System.Text.Encoding.UTF8.GetBytes("proweb-client-session-key");

    public byte[] Protect(byte[] key)
    {
        if (OperatingSystem.IsWindows())
            return ProtectWindows(key);
        return (byte[])key.Clone();
    }

    public byte[] Unprotect(byte[] protectedKey)
    {
        if (OperatingSystem.IsWindows())
            return UnprotectWindows(protectedKey);
        return (byte[])protectedKey.Clone();
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(byte[] key) =>
        ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWindows(byte[] protectedKey) =>
        ProtectedData.Unprotect(protectedKey, Entropy, DataProtectionScope.CurrentUser);
}
