using Microsoft.Data.Sqlite;

namespace Eam.Core.Servers;

/// <summary>Bulk writes used by imports (one transaction, much faster than one connection per row).</summary>
public sealed partial class ServerData
{
    internal void InTransaction(Action<SqliteConnection, SqliteTransaction> work)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        work(c, tx);
        tx.Commit();
    }

    internal static string InventoryHash(string toolbelt, string bag) => Hash(toolbelt, bag);
    internal static string UtcText(DateTime d) => Utc(d);
}
