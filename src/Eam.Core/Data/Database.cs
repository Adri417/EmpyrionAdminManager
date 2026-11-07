using Microsoft.Data.Sqlite;

namespace Eam.Core.Data;

/// <summary>
/// The general EAM database (accounts, settings, list of servers).
/// Each game server will get its own separate database in its own folder (step 2),
/// so the data of two servers can never mix.
/// </summary>
public sealed class Database
{
    private readonly string _connectionString;

    private readonly string[] _migrations;

    public Database(string file) : this(file, Migrations) { }

    public Database(string file, string[] migrations, bool pooling = true)
    {
        _migrations = migrations;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = pooling,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        // WAL + full sync on commit: survives a crash or power cut without corruption.
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    /// <summary>Ordered list of schema changes. Never edit a released entry: append a new one.</summary>
    private static readonly string[] Migrations =
    [
        // 1: accounts
        """
        CREATE TABLE users (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            username      TEXT NOT NULL UNIQUE COLLATE NOCASE,
            password_hash TEXT NOT NULL,
            role          TEXT NOT NULL,
            steam_id      TEXT NULL,
            created_utc   TEXT NOT NULL,
            last_login_utc TEXT NULL,
            security_stamp TEXT NOT NULL
        );
        CREATE TABLE settings (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """,
        // 2: game servers managed by EAM
        """
        CREATE TABLE servers (
            id               INTEGER PRIMARY KEY AUTOINCREMENT,
            slug             TEXT NOT NULL UNIQUE COLLATE NOCASE,
            name             TEXT NOT NULL,
            root_dir         TEXT NOT NULL UNIQUE COLLATE NOCASE,
            game_port        INTEGER NOT NULL UNIQUE,
            telnet_port      INTEGER NOT NULL UNIQUE,
            telnet_password  TEXT NOT NULL,
            branch           TEXT NOT NULL DEFAULT 'public',
            auto_start       INTEGER NOT NULL DEFAULT 0,
            auto_restart     INTEGER NOT NULL DEFAULT 1,
            installed        INTEGER NOT NULL DEFAULT 0,
            build_id         TEXT NULL,
            created_utc      TEXT NOT NULL
        );
        """,
        // 3: secret key of each server's EAM mod
        """
        ALTER TABLE servers ADD COLUMN mod_key TEXT NOT NULL DEFAULT '';
        """,
    ];

    public void Migrate()
    {
        using var c = Open();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version;";
            var current = Convert.ToInt32(cmd.ExecuteScalar());
            for (var i = current; i < _migrations.Length; i++)
            {
                using var tx = c.BeginTransaction();
                using var m = c.CreateCommand();
                m.Transaction = tx;
                m.CommandText = _migrations[i] + $"\nPRAGMA user_version = {i + 1};";
                m.ExecuteNonQuery();
                tx.Commit();
            }
        }
    }

    public int SchemaVersion
    {
        get
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }
}
