using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace local_translate_provider.Services;

/// <summary>
/// Manages CA and server certificates for the API proxy.
/// CA is stored in %LocalAppData%\local-translate-provider\.
/// </summary>
public sealed class CertificateManager
{
    private const string CaFileName = "ca.pfx";
    private const string CaSubject = "CN=Local Translate Provider CA";
    private const string CaPassword = "local-translate-provider-ca";

    private static string CertDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "local-translate-provider");

    private static string CaPath => Path.Combine(CertDirectory, CaFileName);

    /// <summary>
    /// Ensures the CA certificate exists. Creates and persists it if not.
    /// </summary>
    public void EnsureCaExists()
    {
        if (File.Exists(CaPath)) return;

        Directory.CreateDirectory(CertDirectory);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            CaSubject,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                critical: true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using var ca = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));

        var pfxBytes = ca.Export(X509ContentType.Pkcs12, CaPassword);
        File.WriteAllBytes(CaPath, pfxBytes);
    }

    /// <summary>
    /// Gets or creates a server certificate for the given host.
    /// </summary>
    public X509Certificate2 GetOrCreateServerCert(string host)
    {
        EnsureCaExists();

        var safeHost = string.Join("_", host.Split(Path.GetInvalidFileNameChars()));
        var serverPath = Path.Combine(CertDirectory, $"server_{safeHost}.pfx");

        if (File.Exists(serverPath))
        {
            try
            {
                var existing = new X509Certificate2(serverPath, (string?)null, X509KeyStorageFlags.Exportable);
                if (existing.NotAfter > DateTime.UtcNow.AddDays(7))
                    return existing;
            }
            catch { /* Regenerate if corrupted or expired */ }
        }

        using var ca = new X509Certificate2(CaPath, CaPassword, X509KeyStorageFlags.Exportable);
        using var rsa = RSA.Create(2048);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(host);

        var request = new CertificateRequest(
            $"CN={host}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                critical: false));
        request.CertificateExtensions.Add(sanBuilder.Build());

        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= 0x7F;

        using var serverCert = request.Create(
            ca,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(2),
            serial);

        var serverPfx = serverCert.CopyWithPrivateKey(rsa);
        var pfxBytes = serverPfx.Export(X509ContentType.Pkcs12);
        File.WriteAllBytes(serverPath, pfxBytes);

        return new X509Certificate2(pfxBytes, (string?)null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>
    /// Exports the CA certificate to a .cer file at the given path.
    /// </summary>
    public void ExportCaToFile(string path)
    {
        EnsureCaExists();
        using var ca = new X509Certificate2(CaPath, CaPassword, X509KeyStorageFlags.Exportable);
        var cerBytes = ca.Export(X509ContentType.Cert);
        File.WriteAllBytes(path, cerBytes);
    }

    /// <summary>
    /// Tries to install the CA into CurrentUser\Root store. Returns false on failure.
    /// </summary>
    public bool TryInstallCaToCurrentUser()
    {
        try
        {
            EnsureCaExists();
            using var ca = new X509Certificate2(CaPath, CaPassword, X509KeyStorageFlags.Exportable);
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(ca);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the CA certificate for display or export (public key only).
    /// </summary>
    public X509Certificate2 GetCaCertificate()
    {
        EnsureCaExists();
        return new X509Certificate2(CaPath, CaPassword, X509KeyStorageFlags.Exportable);
    }
}
