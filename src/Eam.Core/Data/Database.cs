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
        _gateKey = Path.GetFullPath(file);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = pooling,
        }.ToString();
    }

    /// <summary>
    /// Opens a connection. Inside EAM, one connection at a time works on a given database file:
    /// the others wait their turn (in order) instead of fighting for SQLite's lock. On a slow
    /// Windows disk with many threads, that fight could end in "database is locked" and a lost write.
    /// The wait is limited, so two files opened in opposite orders can never block each other forever.
    /// </summary>
    public SqliteConnection Open()
    {
        var c = new GatedConnection(_connectionString, Gates.GetOrAdd(_gateKey, _ => new object()));
        try
        {
            c.Open();
            using var cmd = c.CreateCommand();
            // WAL + synchronous NORMAL: the database can never be corrupted by a crash or a power cut
            // (SQLite's recommended setting for WAL). Only the very last writes before a power cut may be
            // missing; FULL would sync the disk at every write, which is 30 times slower on Windows.
            // busy_timeout: another program (a database viewer, a backup) holding the file for a moment.
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=30000;";
            cmd.ExecuteNonQuery();
            return c;
        }
        catch { c.Dispose(); throw; }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _gateKey;

    private sealed class GatedConnection : SqliteConnection
    {
        private readonly object _gate;
        private bool _held;

        public GatedConnection(string cs, object gate) : base(cs)
        {
            _gate = gate;
            // Re-entrant: the same thread may open the same database twice (nested calls).
            _held = Monitor.TryEnter(_gate, TimeSpan.FromSeconds(60));
        }

        protected override void Dispose(bool disposing)
        {
            try { base.Dispose(disposing); }
            finally
            {
                if (_held && Monitor.IsEntered(_gate)) Monitor.Exit(_gate);
                _held = false;
            }
        }
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
        // 4: an existing Empyrion folder used in place (server imported from another tool)
        """
        ALTER TABLE servers ADD COLUMN game_dir TEXT NULL;
        """,
        // 5: name shown in the game's server list, when different from the EAM name (folder)
        """
        ALTER TABLE servers ADD COLUMN display_name TEXT NULL;
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
