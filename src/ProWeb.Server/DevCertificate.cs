using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ProWeb.Server;

/// <summary>Generates an in-memory self-signed certificate for development/test HTTPS.</summary>
internal static class DevCertificate
{
    public static X509Certificate2 Create()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=proweb-dev", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());

        var now = DateTimeOffset.UtcNow;
        using var selfSigned = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));

        // Re-import via PFX so the private key is backed by a key set SChannel/Kestrel can use
        // on Windows. Certificates produced by CreateSelfSigned carry an ephemeral key that the
        // Windows TLS stack cannot consume directly.
        var pfx = selfSigned.Export(X509ContentType.Pfx);
        return new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.Exportable);
    }
}
