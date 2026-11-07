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
        if (SessionDays is < 1 or > 365) SessionDays = 14;
        if (Language is not ("auto" or "fr" or "en")) Language = "auto";
    }
}

public sealed class RemoteAccessConfig
{
    /// <summary>Off by default: the interface is only reachable from this machine until an admin turns it on.</summary>
    public bool Enabled { get; set; }

    public int Port { get; set; } = 8791;

    /// <summary>Optional .pfx certificate. Empty = a self-signed certificate is generated in the data folder.</summary>
    public string CertificatePath { get; set; } = "";

    public string CertificatePassword { get; set; } = "";
}
