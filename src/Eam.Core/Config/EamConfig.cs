using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eam.Core.Config;

/// <summary>
/// Settings stored in eam.json. Nothing specific to one installation is hard-coded:
/// every admin edits this file (or the settings page) for their own machine.
/// </summary>
public sealed class EamConfig
{
    /// <summary>Port of the local interface (desktop window and browser on this machine).</summary>
    public int LocalPort { get; set; } = 8790;

    public RemoteAccessConfig RemoteAccess { get; set; } = new();

    /// <summary>"auto" follows the browser language; otherwise "fr" or "en".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Days during which a "remember me" login stays valid.</summary>
    public int SessionDays { get; set; } = 14;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static EamConfig Load(EamPaths paths)
    {
        if (!File.Exists(paths.ConfigFile))
        {
            var fresh = new EamConfig();
            fresh.Save(paths);
            return fresh;
        }

        var json = File.ReadAllText(paths.ConfigFile);
        var cfg = JsonSerializer.Deserialize<EamConfig>(json, JsonOptions) ?? new EamConfig();
        cfg.Validate();
        return cfg;
    }

    /// <summary>Writes atomically: a crash in the middle never leaves a half-written file.</summary>
    public void Save(EamPaths paths)
    {
        Validate();
        Directory.CreateDirectory(paths.DataRoot);
        var tmp = paths.ConfigFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, paths.ConfigFile, overwrite: true);
    }

    public void Validate()
    {
        if (LocalPort is < 1 or > 65535) throw new InvalidDataException("localPort must be between 1 and 65535.");
        if (RemoteAccess.Port is < 1 or > 65535) throw new InvalidDataException("remoteAccess.port must be between 1 and 65535.");
        if (RemoteAccess.Enabled && RemoteAccess.Port == LocalPort)
            throw new InvalidDataException("remoteAccess.port must differ from localPort.");
        if (RemoteAccess.ProxyPort != 0 && (RemoteAccess.ProxyPort is < 1 or > 65535 || RemoteAccess.ProxyPort == LocalPort
                                            || (RemoteAccess.Enabled && RemoteAccess.ProxyPort == RemoteAccess.Port)))
            throw new InvalidDataException("remoteAccess.proxyPort must be 0 or a free port different from the other ports.");
        if (SessionDays is < 1 or > 365) SessionDays = 14;
        var le = RemoteAccess.LetsEncrypt;
        if (le.Enabled)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(le.Domain, @"^(?=.{4,253}$)([a-zA-Z0-9-]{1,63}\.)+[a-zA-Z]{2,63}$")) throw new InvalidDataException("letsEncrypt.domain is not a valid domain name.");
            if (!le.Email.Contains('@')) throw new InvalidDataException("letsEncrypt.email is required.");
            if (!le.AcceptTerms) throw new InvalidDataException("Let's Encrypt terms must be accepted.");
            if (le.HttpPort is < 1 or > 65535 || le.HttpPort == LocalPort || le.HttpPort == RemoteAccess.Port) throw new InvalidDataException("letsEncrypt.httpPort is invalid.");
        }
        if (Language is not ("auto" or "fr" or "en")) Language = "auto";
    }
}

public sealed class RemoteAccessConfig
{
    /// <summary>Off by default: the interface is only reachable from this machine until an admin turns it on.</summary>
    public bool Enabled { get; set; }

    public int Port { get; set; } = 8791;

    /// <summary>
    /// Port for a reverse proxy on this machine (Apache, nginx, IIS) that already serves HTTPS for a domain.
    /// Listens on 127.0.0.1 only, plain HTTP; requests on it are treated as remote. 0 = off.
    /// </summary>
    public int ProxyPort { get; set; }

    /// <summary>Optional .pfx certificate. Empty = a self-signed certificate is generated in the data folder.</summary>
    public string CertificatePath { get; set; } = "";

    public string CertificatePassword { get; set; } = "";

    /// <summary>Free certificate from Let's Encrypt for a domain name pointing to this machine (port 80 must reach it).</summary>
    public LetsEncryptConfig LetsEncrypt { get; set; } = new();
}

public sealed class LetsEncryptConfig
{
    public bool Enabled { get; set; }
    public string Domain { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>Port where Let's Encrypt checks the domain (80, or another port forwarded from 80 by the router).</summary>
    public int HttpPort { get; set; } = 80;
    public bool AcceptTerms { get; set; }
}
