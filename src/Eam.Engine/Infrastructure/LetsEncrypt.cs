using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Eam.Core.Config;

namespace Eam.Engine.Infrastructure;

/// <summary>The HTTPS certificate in use; replaced without restarting when Let's Encrypt renews it.</summary>
public sealed class CertificateHolder
{
    public X509Certificate2? Current { get; set; }
}

/// <summary>
/// Step 9: free HTTPS certificate from Let's Encrypt (HTTP-01 check). Asked when missing, renewed 30 days
/// before it expires. The certificate and the account key stay in the data folder.
/// </summary>
public sealed class LetsEncryptService(EamPaths paths, EamConfig config, CertificateHolder holder, ILogger<LetsEncryptService> log) : BackgroundService
{
    /// <summary>Token → answer, served on http://domain/.well-known/acme-challenge/token while a check runs.</summary>
    public static readonly ConcurrentDictionary<string, string> Challenges = new();

    public string? LastError { get; private set; }
    public DateTime? Expires => holder.Current?.NotAfter.ToUniversalTime();

    public static string PfxFile(EamPaths paths) => Path.Combine(paths.CertificatesDir, "letsencrypt.pfx");

    /// <summary>The stored Let's Encrypt certificate, if it matches the domain and is still valid.</summary>
    public static X509Certificate2? LoadStored(EamPaths paths, LetsEncryptConfig cfg)
    {
        var file = PfxFile(paths);
        if (!File.Exists(file)) return null;
        try
        {
            var cert = new X509Certificate2(file, (string?)null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
            return cert.NotAfter > DateTime.Now && cert.GetNameInfo(X509NameType.DnsName, false).Equals(cfg.Domain, StringComparison.OrdinalIgnoreCase) ? cert : null;
        }
        catch { return null; }
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var le = config.RemoteAccess.LetsEncrypt;
        if (!config.RemoteAccess.Enabled || !le.Enabled) return;
        await Task.Delay(TimeSpan.FromSeconds(10), stop); // let the web server start first
        while (!stop.IsCancellationRequested)
        {
            var current = LoadStored(paths, le);
            if (current is null || current.NotAfter < DateTime.Now.AddDays(30))
            {
                try
                {
                    var cert = await OrderAsync(le, stop);
                    holder.Current = cert;
                    LastError = null;
                    log.LogInformation("Let's Encrypt certificate for {Domain} valid until {Until}", le.Domain, cert.NotAfter);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    LastError = e.Message;
                    log.LogWarning("Let's Encrypt failed for {Domain}: {Err}", le.Domain, e.Message);
                    await Task.Delay(TimeSpan.FromHours(1), stop);
                    continue;
                }
            }
            await Task.Delay(TimeSpan.FromHours(12), stop);
        }
    }

    private async Task<X509Certificate2> OrderAsync(LetsEncryptConfig le, CancellationToken stop)
    {
        System.IO.Directory.CreateDirectory(paths.CertificatesDir);
        var keyFile = Path.Combine(paths.CertificatesDir, "acme-account.pem");
        AcmeContext acme;
        if (File.Exists(keyFile)) acme = new AcmeContext(WellKnownServers.LetsEncryptV2, KeyFactory.FromPem(await File.ReadAllTextAsync(keyFile, stop)));
        else
        {
            acme = new AcmeContext(WellKnownServers.LetsEncryptV2);
            await acme.NewAccount(le.Email, termsOfServiceAgreed: true);
            await File.WriteAllTextAsync(keyFile, acme.AccountKey.ToPem(), stop);
        }
        var order = await acme.NewOrder([le.Domain]);
        var authz = (await order.Authorizations()).First();
        var http = await authz.Http();
        Challenges[http.Token] = http.KeyAuthz;
        try
        {
            await http.Validate();
            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stop);
                var res = await authz.Resource();
                if (res.Status == AuthorizationStatus.Valid) break;
                if (res.Status == AuthorizationStatus.Invalid)
                    throw new InvalidOperationException($"domain check failed: is http://{le.Domain}:{le.HttpPort}/ reachable from the Internet (port 80 forwarded)?");
            }
        }
        finally { Challenges.TryRemove(http.Token, out _); }

        var key = KeyFactory.NewKey(KeyAlgorithm.RS256);
        var chain = await order.Generate(new CsrInfo { CommonName = le.Domain }, key);
        using var pem = X509Certificate2.CreateFromPem(chain.Certificate.ToPem(), key.ToPem());
        var bytes = pem.Export(X509ContentType.Pfx);
        var file = PfxFile(paths);
        await File.WriteAllBytesAsync(file + ".tmp", bytes, stop);
        File.Move(file + ".tmp", file, overwrite: true);
        return new X509Certificate2(bytes, (string?)null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
    }
}
