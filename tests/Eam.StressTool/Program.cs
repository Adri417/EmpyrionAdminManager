using Eam.Core.Servers;

// Usage: Eam.StressTool <server.db> <tag>
// Writes chat, players, inventories and journal lines in a loop with 4 threads until killed.
var file = args[0];
var tag = args[1];
var data = new ServerData(file);
Console.WriteLine("ready");
Console.Out.Flush();
var threads = Enumerable.Range(0, 4).Select(t => new Thread(() =>
{
    for (var i = 0; ; i++)
    {
        var id = $"{tag}-{t}-{i}";
        data.AddChat(DateTime.UtcNow, "Global", t, $"{tag}-player{t}", "7656119" + t, 0, id, "stress");
        data.PlayerSeen($"7656{t}{i % 50}", t, $"{tag}-player{t}", 1);
        data.AddInventorySnapshot($"7656{t}", $"{tag}-player{t}", "manual", "Akua", $"[{{\"id\":{i},\"count\":1,\"slot\":1}}]", "[]", DateTime.UtcNow);
        data.InTransaction((c, tx) =>
        {
            for (var k = 0; k < 20; k++)
            {
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO chat (utc, channel, sender_id, sender_name, sender_steam, recipient_id, text, source) VALUES ($u,'Global',0,$n,NULL,0,$t,'batch');";
                cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("$n", tag);
                cmd.Parameters.AddWithValue("$t", $"{id}-batch-{k}");
                cmd.ExecuteNonQuery();
            }
        });
    }
}) { IsBackground = true }).ToList();
threads.ForEach(t => t.Start());
threads.ForEach(t => t.Join());
