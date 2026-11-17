using System.Text.Json;
using System.Text.Json.Nodes;

namespace Eam.Core.Servers;

/// <summary>
/// Vita Somnium Life configuration: server_config.json (name, ports, players, password...) and
/// databaseinfo.json (its MySQL database). Keys EAM does not know are kept as they are.
/// </summary>
public static class VslConfig
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static JsonObject Load(string file)
    {
        try { return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? [] : []; }
        catch (JsonException) { return []; }
    }

    public static void Save(string file, JsonObject json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file + ".tmp", json.ToJsonString(Pretty));
        File.Move(file + ".tmp", file, overwrite: true);
    }

    /// <summary>The values EAM manages: name in the list, game port and query port.</summary>
    public static void ApplyManaged(ServerDefinition def)
    {
        var j = Load(def.VslConfigFile);
        j["serverName"] = def.ListName;
        j["port"] = def.GamePort;
        j["queryPort"] = def.TelnetPort;
        Save(def.VslConfigFile, j);
    }

    public static void ApplySettings(ServerDefinition def, int maxPlayers, string? password)
    {
        var j = Load(def.VslConfigFile);
        j["maxPlayers"] = maxPlayers;
        if (password is not null) j["password"] = password.Trim();
        Save(def.VslConfigFile, j);
    }

    public static int MaxPlayers(ServerDefinition def) =>
        Load(def.VslConfigFile)["maxPlayers"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;

    public sealed record Database(string Host, int Port, string Name, string User, string? Password);

    public static Database? ReadDatabase(ServerDefinition def)
    {
        var j = Load(def.VslDatabaseFile);
        if (j.Count == 0) return null;
        string S(string k) => j[k]?.GetValue<object>()?.ToString() ?? "";
        return new Database(S("host"), int.TryParse(S("port"), out var p) ? p : 3306, S("database"), S("username"), null);
    }

    /// <summary>The MySQL connection with its password, for EAM's own use only (never sent to a browser).</summary>
    public static (Database Db, string Password)? ReadDatabaseWithPassword(ServerDefinition def)
    {
        var db = ReadDatabase(def);
        if (db is null) return null;
        var pw = Load(def.VslDatabaseFile)["password"]?.GetValue<object>()?.ToString() ?? "";
        return (db, pw);
    }

    public static bool HasPassword(ServerDefinition def) =>
        Load(def.VslDatabaseFile)["password"]?.GetValue<object>()?.ToString() is { Length: > 0 };

    /// <summary>Keys EAM writes itself from the server's settings (name, ports): edited with Rename and Settings.</summary>
    public static readonly string[] ManagedKeys = ["serverName", "port", "queryPort"];

    /// <summary>Writes every given key of server_config.json except the managed ones; unknown keys are kept.</summary>
    public static void ApplyRaw(ServerDefinition def, JsonObject changes)
    {
        var j = Load(def.VslConfigFile);
        foreach (var (k, v) in changes)
        {
            if (ManagedKeys.Contains(k) || string.IsNullOrWhiteSpace(k) || k.Length > 100) continue;
            j[k] = v?.DeepClone();
        }
        Save(def.VslConfigFile, j);
    }

    /// <summary>Writes the MySQL connection (password only when given, so it is never shown back).</summary>
    public static void WriteDatabase(ServerDefinition def, Database db)
    {
        var j = Load(def.VslDatabaseFile);
        j["host"] = string.IsNullOrWhiteSpace(db.Host) ? "127.0.0.1" : db.Host.Trim();
        j["port"] = db.Port is > 0 and < 65536 ? db.Port : 3306;
        j["database"] = db.Name.Trim();
        j["username"] = db.User.Trim();
        if (db.Password is not null) j["password"] = db.Password;
        j["charset"] ??= "utf8mb4";
        Save(def.VslDatabaseFile, j);
    }
}
