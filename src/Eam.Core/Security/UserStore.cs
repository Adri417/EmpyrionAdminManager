using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Eam.Core.Data;

namespace Eam.Core.Security;

public sealed record User(long Id, string Username, string Role, string? SteamId, DateTime CreatedUtc, DateTime? LastLoginUtc, string SecurityStamp);

public sealed class UserStoreException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>Accounts of the people allowed to use EAM.</summary>
public sealed partial class UserStore(Database db)
{
    private static readonly object CreateLock = new();

    [GeneratedRegex("^[A-Za-z0-9_.\\-]{3,32}$")]
    private static partial Regex UsernameRegex();

    [GeneratedRegex("^[0-9]{17}$")]
    private static partial Regex SteamIdRegex();

    public int Count()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public bool NeedsSetup => Count() == 0;

    /// <summary>Creates the very first account (owner). Fails if any account already exists.</summary>
    public User CreateFirstOwner(string username, string password)
    {
        lock (CreateLock)
        {
            if (Count() != 0) throw new UserStoreException("already_set_up");
            return CreateInternal(username, password, Roles.Owner, null);
        }
    }

    public User Create(string username, string password, string role, string? steamId)
    {
        lock (CreateLock)
            return CreateInternal(username, password, role, steamId);
    }

    private User CreateInternal(string username, string password, string role, string? steamId)
    {
        username = (username ?? "").Trim();
        if (!UsernameRegex().IsMatch(username)) throw new UserStoreException("invalid_username");
        if (PasswordHasher.CheckStrength(password) is { } err) throw new UserStoreException(err);
        if (!Roles.IsValid(role)) throw new UserStoreException("invalid_role");
        steamId = NormalizeSteamId(steamId);
        if (FindByName(username) is not null) throw new UserStoreException("username_taken");

        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO users (username, password_hash, role, steam_id, created_utc, security_stamp)
            VALUES ($u, $p, $r, $s, $c, $st) RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$u", username);
        cmd.Parameters.AddWithValue("$p", PasswordHasher.Hash(password));
        cmd.Parameters.AddWithValue("$r", role);
        cmd.Parameters.AddWithValue("$s", (object?)steamId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$st", NewStamp());
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        return FindById(id)!;
    }

    private static string? NormalizeSteamId(string? steamId)
    {
        steamId = string.IsNullOrWhiteSpace(steamId) ? null : steamId.Trim();
        if (steamId is not null && !SteamIdRegex().IsMatch(steamId)) throw new UserStoreException("invalid_steam_id");
        return steamId;
    }

    /// <summary>Returns the user if the password matches, otherwise null.</summary>
    public User? CheckPassword(string username, string password)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, password_hash FROM users WHERE username = $u;";
        cmd.Parameters.AddWithValue("$u", (username ?? "").Trim());
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            // Same cost as a real check so the response time does not reveal which usernames exist.
            PasswordHasher.Verify(password ?? "", DummyHash);
            return null;
        }
        var id = r.GetInt64(0);
        var hash = r.GetString(1);
        r.Close();
        if (!PasswordHasher.Verify(password ?? "", hash)) return null;

        using var upd = c.CreateCommand();
        upd.CommandText = "UPDATE users SET last_login_utc = $t WHERE id = $id;";
        upd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        upd.Parameters.AddWithValue("$id", id);
        upd.ExecuteNonQuery();
        return FindById(id);
    }

    private static readonly string DummyHash = PasswordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)));

    public User? FindById(long id) => QueryOne("WHERE id = $v", id);
    public User? FindByName(string username) => QueryOne("WHERE username = $v", username.Trim());

    public IReadOnlyList<User> List()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"{SelectSql} ORDER BY username;";
        using var r = cmd.ExecuteReader();
        var list = new List<User>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    public void ChangePassword(long id, string newPassword)
    {
        if (PasswordHasher.CheckStrength(newPassword) is { } err) throw new UserStoreException(err);
        Exec("UPDATE users SET password_hash = $a, security_stamp = $b WHERE id = $id;", id, PasswordHasher.Hash(newPassword), NewStamp());
    }

    public void Update(long id, string role, string? steamId)
    {
        if (!Roles.IsValid(role)) throw new UserStoreException("invalid_role");
        steamId = NormalizeSteamId(steamId);
        lock (CreateLock)
        {
            var user = FindById(id) ?? throw new UserStoreException("not_found");
            if (user.Role == Roles.Owner && role != Roles.Owner && CountOwners() <= 1)
                throw new UserStoreException("last_owner");
            // A role change invalidates the sessions of that user.
            var stamp = user.Role == role ? user.SecurityStamp : NewStamp();
            Exec("UPDATE users SET role = $a, steam_id = $b, security_stamp = $c WHERE id = $id;", id, role, steamId, stamp);
        }
    }

    public void Delete(long id)
    {
        lock (CreateLock)
        {
            var user = FindById(id) ?? throw new UserStoreException("not_found");
            if (user.Role == Roles.Owner && CountOwners() <= 1) throw new UserStoreException("last_owner");
            Exec("DELETE FROM users WHERE id = $id;", id);
        }
    }

    private int CountOwners()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users WHERE role = $r;";
        cmd.Parameters.AddWithValue("$r", Roles.Owner);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private const string SelectSql = "SELECT id, username, role, steam_id, created_utc, last_login_utc, security_stamp FROM users";

    private User? QueryOne(string where, object value)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"{SelectSql} {where};";
        cmd.Parameters.AddWithValue("$v", value);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Map(r) : null;
    }

    private static User Map(Microsoft.Data.Sqlite.SqliteDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
        DateTime.Parse(r.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
        r.IsDBNull(5) ? null : DateTime.Parse(r.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
        r.GetString(6));

    private void Exec(string sql, long id, params object?[] values)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", id);
        var names = new[] { "$a", "$b", "$c" };
        for (var i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue(names[i], values[i] ?? DBNull.Value);
        if (cmd.ExecuteNonQuery() == 0) throw new UserStoreException("not_found");
    }

    private static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}
