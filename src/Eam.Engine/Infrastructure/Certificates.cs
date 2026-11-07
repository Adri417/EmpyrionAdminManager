using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Eam.Core.Config;

namespace Eam.Engine.Infrastructure;

/// <summary>Provides the HTTPS certificate used for remote access.</summary>
public static class Certificates
{
    public static X509Certificate2 LoadOrCreate(EamPaths paths, RemoteAccessConfig cfg, ILogger log)
    {
        if (!string.IsNullOrWhiteSpace(cfg.CertificatePath))
        {
            log.LogInformation("Using HTTPS certificate {Path}", cfg.CertificatePath);
            return new X509Certificate2(cfg.CertificatePath, cfg.CertificatePassword, X509KeyStorageFlags.MachineKeySet);
        }

        var file = Path.Combine(paths.CertificatesDir, "self-signed.pfx");
        if (File.Exists(file))
        {
            var existing = new X509Certificate2(file, (string?)null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
            if (existing.NotAfter > DateTime.Now.AddDays(30)) return existing;
            log.LogInformation("Self-signed certificate expires soon, generating a new one");
        }

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={Environment.MachineName}, O=Empyrion Admin Manager", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(Environment.MachineName);
        san.AddDnsName("localhost");
        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));
        var bytes = cert.Export(X509ContentType.Pfx);
        var tmp = file + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, file, overwrite: true);
        log.LogInformation("Generated a self-signed HTTPS certificate in {File}", file);
        return new X509Certificate2(bytes, (string?)null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
    }
}
