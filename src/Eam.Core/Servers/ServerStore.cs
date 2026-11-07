using System.Globalization;
using Eam.Core.Data;
using Microsoft.Data.Sqlite;

namespace Eam.Core.Servers;

/// <summary>List of the servers managed by EAM (general database).</summary>
public sealed class ServerStore(Database db)
{
    private const string Columns = "id, slug, name, root_dir, game_port, telnet_port, telnet_password, branch, auto_start, auto_restart, installed, build_id, created_utc, mod_key, game_dir";

    public IReadOnlyList<ServerDefinition> List()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM servers ORDER BY name COLLATE NOCASE;";
        using var r = cmd.ExecuteReader();
        var list = new List<ServerDefinition>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public ServerDefinition? Find(long id) => List().FirstOrDefault(s => s.Id == id);

    public ServerDefinition Add(ServerDefinition s)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO servers (slug, name, root_dir, game_port, telnet_port, telnet_password, branch, auto_start, auto_restart, installed, build_id, created_utc, mod_key, game_dir)
            VALUES ($slug, $name, $root, $gp, $tp, $tpw, $branch, $as, $ar, $inst, $build, $created, $mk, $gd) RETURNING id;
            """;
        Bind(cmd, s);
        cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        return Find(id)!;
    }

    public ServerDefinition Update(ServerDefinition s)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE servers SET slug=$slug, name=$name, root_dir=$root, game_port=$gp, telnet_port=$tp, telnet_password=$tpw,
              branch=$branch, auto_start=$as, auto_restart=$ar, installed=$inst, build_id=$build, mod_key=$mk, game_dir=$gd WHERE id=$id;
            """;
        Bind(cmd, s);
        cmd.Parameters.AddWithValue("$id", s.Id);
        cmd.ExecuteNonQuery();
        return Find(s.Id)!;
    }

    public void Remove(long id)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM servers WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand cmd, ServerDefinition s)
    {
        cmd.Parameters.AddWithValue("$slug", s.Slug);
        cmd.Parameters.AddWithValue("$name", s.Name);
        cmd.Parameters.AddWithValue("$root", s.RootDir);
        cmd.Parameters.AddWithValue("$gp", s.GamePort);
        cmd.Parameters.AddWithValue("$tp", s.TelnetPort);
        cmd.Parameters.AddWithValue("$tpw", s.TelnetPassword);
        cmd.Parameters.AddWithValue("$branch", s.Branch);
        cmd.Parameters.AddWithValue("$as", s.AutoStart ? 1 : 0);
        cmd.Parameters.AddWithValue("$ar", s.AutoRestart ? 1 : 0);
        cmd.Parameters.AddWithValue("$inst", s.Installed ? 1 : 0);
        cmd.Parameters.AddWithValue("$build", (object?)s.BuildId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mk", s.ModKey ?? "");
        cmd.Parameters.AddWithValue("$gd", (object?)s.GameDirOverride ?? DBNull.Value);
    }

    private static ServerDefinition Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Slug = r.GetString(1),
        Name = r.GetString(2),
        RootDir = r.GetString(3),
        GamePort = r.GetInt32(4),
        TelnetPort = r.GetInt32(5),
        TelnetPassword = r.GetString(6),
        Branch = r.GetString(7),
        AutoStart = r.GetInt32(8) != 0,
        AutoRestart = r.GetInt32(9) != 0,
        Installed = r.GetInt32(10) != 0,
        BuildId = r.IsDBNull(11) ? null : r.GetString(11),
        CreatedUtc = DateTime.Parse(r.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        ModKey = r.GetString(13),
        GameDirOverride = r.IsDBNull(14) ? null : r.GetString(14),
    };
}
