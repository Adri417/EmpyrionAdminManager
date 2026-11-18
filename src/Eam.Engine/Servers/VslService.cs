using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Eam.Core.Servers;
using MySqlConnector;

namespace Eam.Engine.Servers;

public sealed record VslDatabaseRequest(string? Host, int? Port, string? Database, string? Username, string? Password);
public sealed record VslRowUpdate(Dictionary<string, JsonElement> Key, Dictionary<string, JsonElement> Set);

/// <summary>
/// Vita Somnium Life: every key of server_config.json, the database connection, and the game's own MySQL data
/// (read from information_schema: the game creates its tables itself on first start).
/// </summary>
public sealed class VslService(ServerStore store, ServerManager manager)
{
    private ServerDefinition Get(long id)
    {
        var def = store.Find(id) ?? throw new ServerException("not_found");
        if (!def.IsVsl) throw new ServerException("not_vsl");
        return def;
    }

    // ------------------------------------------------------------------ configuration files

    public object Config(long id)
    {
        var def = Get(id);
        var db = VslConfig.ReadDatabase(def);
        return new
        {
            config = VslConfig.Load(def.VslConfigFile),
            managed = VslConfig.ManagedKeys,
            database = db is null ? null : new { host = db.Host, port = db.Port, database = db.Name, username = db.User, hasPassword = VslConfig.HasPassword(def) },
        };
    }

    public void SaveConfig(long id, JsonObject changes, string by)
    {
        var def = Get(id);
        if (!File.Exists(def.VslConfigFile)) throw new ServerException("not_installed");
        VslConfig.ApplyRaw(def, changes);
        ServerManager.ServerLog(def, $"server_config.json changed by {by}: {string.Join(", ", changes.Select(c => c.Key))}");
    }

    public void SaveDatabase(long id, VslDatabaseRequest req, string by)
    {
        var def = Get(id);
        if (string.IsNullOrWhiteSpace(req.Database)) throw new ServerException("vsl_database_required");
        VslConfig.WriteDatabase(def, new VslConfig.Database(req.Host ?? "127.0.0.1", req.Port ?? 3306, req.Database, req.Username ?? "", string.IsNullOrEmpty(req.Password) ? null : req.Password));
        ServerManager.ServerLog(def, $"databaseinfo.json changed by {by}");
    }

    /// <summary>Who is online, from the Steam query port (one small UDP request, nothing asked to the game).</summary>
    public async Task<object> OnlineAsync(long id)
    {
        var def = Get(id);
        var info = await SteamQuery.InfoAsync(def.TelnetPort);
        var players = info is null ? null : await SteamQuery.PlayersAsync(def.TelnetPort);
        return new
        {
            answered = info is not null,
            count = info?.Players,
            max = info?.MaxPlayers,
            map = info?.Map,
            players = players?.Select(p => new { name = p.Name, seconds = (int)p.Seconds }),
        };
    }

    // ------------------------------------------------------------------ the game's MySQL data

    private static string Q(string name) => "`" + name.Replace("`", "``") + "`";

    private static readonly Regex Ident = new("^[A-Za-z0-9_$]{1,64}$");

    private static async Task<MySqlConnection> OpenAsync(ServerDefinition def)
    {
        var info = VslConfig.ReadDatabaseWithPassword(def) ?? throw new ServerException("vsl_no_database");
        var (db, pw) = info;
        var cs = new MySqlConnectionStringBuilder
        {
            Server = string.IsNullOrWhiteSpace(db.Host) ? "127.0.0.1" : db.Host,
            Port = (uint)db.Port,
            Database = db.Name,
            UserID = db.User,
            Password = pw,
            ConnectionTimeout = 5,
            DefaultCommandTimeout = 20,
            CharacterSet = "utf8mb4",
            ConvertZeroDateTime = true,
        };
        var c = new MySqlConnection(cs.ConnectionString);
        try { await c.OpenAsync(); }
        catch (MySqlException e) { await c.DisposeAsync(); throw new ServerException("vsl_db_connect", e.Message); }
        return c;
    }

    public sealed record Column(string Name, string Type, bool Key, bool Nullable, bool Auto);

    private static async Task<Dictionary<string, List<Column>>> SchemaAsync(MySqlConnection c)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, COLUMN_KEY, IS_NULLABLE, EXTRA
            FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME, ORDINAL_POSITION;
            """;
        var map = new Dictionary<string, List<Column>>(StringComparer.Ordinal);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var t = r.GetString(0);
            if (!map.TryGetValue(t, out var cols)) map[t] = cols = [];
            cols.Add(new Column(r.GetString(1), r.GetString(2), r.GetString(3) == "PRI", r.GetString(4) == "YES",
                r.GetString(5).Contains("auto_increment", StringComparison.OrdinalIgnoreCase)));
        }
        return map;
    }

    public async Task<object> TablesAsync(long id)
    {
        await using var c = await OpenAsync(Get(id));
        var schema = await SchemaAsync(c);
        var counts = new Dictionary<string, long>();
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT TABLE_NAME, COALESCE(TABLE_ROWS,0) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE();";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) counts[r.GetString(0)] = Convert.ToInt64(r.GetValue(1));
        }
        return new
        {
            database = c.Database,
            tables = schema.Select(t => new { name = t.Key, rows = counts.GetValueOrDefault(t.Key), columns = t.Value, editable = t.Value.Any(x => x.Key) }),
        };
    }

    public async Task<object> RowsAsync(long id, string table, string? q, int offset, int limit)
    {
        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);
        await using var c = await OpenAsync(Get(id));
        var cols = await ColumnsOf(c, table);
        var where = "";
        await using var cmd = c.CreateCommand();
        if (!string.IsNullOrWhiteSpace(q))
        {
            where = " WHERE " + string.Join(" OR ", cols.Select(x => $"CAST({Q(x.Name)} AS CHAR) LIKE @q"));
            cmd.Parameters.AddWithValue("@q", "%" + q.Trim() + "%");
        }
        var order = cols.Where(x => x.Key).Select(x => Q(x.Name)).ToList();
        cmd.CommandText = $"SELECT COUNT(*) FROM `{table}`{where};";
        var total = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        cmd.CommandText = $"SELECT * FROM `{table}`{where}{(order.Count > 0 ? " ORDER BY " + string.Join(", ", order) : "")} LIMIT {limit} OFFSET {offset};";
        var rows = new List<object?[]>();
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                var row = new object?[r.FieldCount];
                for (var i = 0; i < r.FieldCount; i++) row[i] = Cell(r.IsDBNull(i) ? null : r.GetValue(i));
                rows.Add(row);
            }
        return new { table, columns = cols, total, offset, rows };
    }

    private static object? Cell(object? v) => v switch
    {
        null => null,
        byte[] b => b.Length <= 64 ? "0x" + Convert.ToHexString(b) : $"<{b.Length} bytes>",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        TimeSpan t => t.ToString(),
        bool b => b ? 1 : 0,
        _ => v,
    };

    private static async Task<List<Column>> ColumnsOf(MySqlConnection c, string table)
    {
        if (!Ident.IsMatch(table ?? "")) throw new ServerException("vsl_bad_table");
        var schema = await SchemaAsync(c);
        return schema.TryGetValue(table!, out var cols) ? cols : throw new ServerException("vsl_bad_table");
    }

    /// <summary>Changes one row, found by its full primary key. Every change is written to the server's EAM log with its old value.</summary>
    public async Task<object> UpdateRowAsync(long id, string table, VslRowUpdate req, string by)
    {
        var def = Get(id);
        await using var c = await OpenAsync(def);
        var cols = await ColumnsOf(c, table);
        var keys = cols.Where(x => x.Key).ToList();
        if (keys.Count == 0) throw new ServerException("vsl_no_primary_key");
        if (req.Set is null || req.Set.Count == 0) return new { changed = 0 };
        if (keys.Any(k => !req.Key.ContainsKey(k.Name))) throw new ServerException("vsl_bad_key");
        var byName = cols.ToDictionary(x => x.Name);
        foreach (var name in req.Set.Keys)
            if (!byName.ContainsKey(name)) throw new ServerException("vsl_bad_column", name);

        await using var tx = await c.BeginTransactionAsync();
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        var where = string.Join(" AND ", keys.Select((k, i) => $"{Q(k.Name)} <=> @k{i}"));
        for (var i = 0; i < keys.Count; i++) cmd.Parameters.AddWithValue($"@k{i}", Value(req.Key[keys[i].Name]));

        // old values, for the log (and to make sure exactly one row is concerned)
        cmd.CommandText = $"SELECT {string.Join(", ", req.Set.Keys.Select(Q))} FROM `{table}` WHERE {where} FOR UPDATE;";
        var old = new List<object?[]>();
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                var row = new object?[r.FieldCount];
                for (var i = 0; i < r.FieldCount; i++) row[i] = Cell(r.IsDBNull(i) ? null : r.GetValue(i));
                old.Add(row);
            }
        if (old.Count != 1) throw new ServerException("vsl_row_not_found");

        var sets = req.Set.Keys.Select((n, i) => $"{Q(n)} = @s{i}").ToList();
        var j = 0;
        foreach (var v in req.Set.Values) cmd.Parameters.AddWithValue($"@s{j++}", Value(v));
        cmd.CommandText = $"UPDATE `{table}` SET {string.Join(", ", sets)} WHERE {where} LIMIT 1;";
        try { await cmd.ExecuteNonQueryAsync(); }
        catch (MySqlException e) { throw new ServerException("vsl_db_refused", e.Message); }
        await tx.CommitAsync();

        var keyText = string.Join(", ", keys.Select(k => $"{k.Name}={req.Key[k.Name]}"));
        var changes = string.Join(", ", req.Set.Keys.Select((n, i) => $"{n}: {old[0][i] ?? "NULL"} -> {req.Set[n]}"));
        ServerManager.ServerLog(def, $"Database {table} ({keyText}) changed by {by}: {changes}");
        return new { changed = 1 };
    }

    /// <summary>
    /// Empties every table of the game's database (the structure stays: the game finds its tables at the next start).
    /// The server must be stopped. All rows are first saved in eam\vsl-reset-backups\&lt;time&gt;.json.gz.
    /// </summary>
    public async Task<object> ResetDatabaseAsync(long id, string confirmName, string by)
    {
        var def = Get(id);
        if (!string.Equals(confirmName?.Trim(), def.Name, StringComparison.Ordinal)) throw new ServerException("confirm_name_mismatch");
        var state = manager.Runtime(id).State;
        if (state is not (ServerState.Stopped or ServerState.Crashed or ServerState.NotInstalled)) throw new ServerException("vsl_reset_running");
        await using var c = await OpenAsync(def);
        var tables = (await SchemaAsync(c)).Keys.ToList();

        var dir = Path.Combine(def.EamDir, "vsl-reset-backups");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json.gz");
        long saved = 0;
        await using (var fs = File.Create(file))
        await using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest))
        await using (var w = new Utf8JsonWriter(gz))
        {
            w.WriteStartObject();
            w.WriteString("database", c.Database);
            w.WriteString("utc", DateTime.UtcNow.ToString("O"));
            w.WriteStartObject("tables");
            foreach (var t in tables)
            {
                await using var cmd = c.CreateCommand();
                cmd.CommandText = $"SELECT * FROM {Q(t)};";
                await using var r = await cmd.ExecuteReaderAsync();
                w.WriteStartArray(t);
                while (await r.ReadAsync())
                {
                    w.WriteStartObject();
                    for (var i = 0; i < r.FieldCount; i++)
                    {
                        w.WritePropertyName(r.GetName(i));
                        var v = r.IsDBNull(i) ? null : r.GetValue(i);
                        if (v is byte[] b) w.WriteStringValue("base64:" + Convert.ToBase64String(b));
                        else JsonSerializer.Serialize(w, Cell(v));
                    }
                    w.WriteEndObject();
                    saved++;
                }
                w.WriteEndArray();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }

        await using (var cmd = c.CreateCommand())
        {
            try
            {
                cmd.CommandText = "SET FOREIGN_KEY_CHECKS = 0;";
                await cmd.ExecuteNonQueryAsync();
                foreach (var t in tables)
                {
                    cmd.CommandText = $"TRUNCATE TABLE {Q(t)};";
                    try { await cmd.ExecuteNonQueryAsync(); }
                    catch (MySqlException) { cmd.CommandText = $"DELETE FROM {Q(t)};"; await cmd.ExecuteNonQueryAsync(); }
                }
            }
            catch (MySqlException e) { throw new ServerException("vsl_db_refused", e.Message); }
            finally { cmd.CommandText = "SET FOREIGN_KEY_CHECKS = 1;"; await cmd.ExecuteNonQueryAsync(); }
        }
        ServerManager.ServerLog(def, $"Database reset by {by}: {tables.Count} tables emptied, {saved} rows saved in {file}");
        return new { tables = tables.Count, rows = saved, backup = file };
    }

    private static object? Value(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => 1,
        JsonValueKind.False => 0,
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDecimal(),
        JsonValueKind.String => e.GetString(),
        _ => e.GetRawText(),
    };
}
