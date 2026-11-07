using System.Diagnostics;
using Eam.Core.Servers;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Eam.Tests;

/// <summary>
/// Anti-corruption checks: two servers written at the same time by many threads, and a writer
/// process killed in the middle of its writes. Each database must stay intact and hold only its own data.
/// </summary>
public class StressTests(ITestOutputHelper output)
{
    private static string IntegrityCheck(string file)
    {
        using var c = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        var r = string.Join(" | ", ReadAll(cmd));
        cmd.CommandText = "PRAGMA foreign_key_check;";
        var fk = ReadAll(cmd).Count;
        return fk == 0 ? r : r + $" | {fk} foreign key errors";
    }

    private static List<string> ReadAll(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetValue(0)?.ToString() ?? "");
        return l;
    }

    private static long Scalar(string file, string sql)
    {
        using var c = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void TwoServersWrittenAtTheSameTimeStayIntactAndSeparate()
    {
        using var dir = new TempDir();
        string[] tags = ["A", "B"];
        var files = tags.ToDictionary(t => t, t => Path.Combine(dir.Path, t, "eam", "server.db"));
        var data = tags.ToDictionary(t => t, t => new ServerData(files[t]));
        const int writers = 8, ops = 150;
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
        var sw = Stopwatch.StartNew();

        var threads = new List<Thread>();
        foreach (var tag in tags)
        {
            var d = data[tag];
            for (var w = 0; w < writers; w++)
            {
                var wi = w;
                threads.Add(new Thread(() =>
                {
                    try
                    {
                        for (var i = 0; i < ops; i++)
                        {
                            var id = $"{tag}-{wi}-{i}";
                            d.AddChat(DateTime.UtcNow, "Global", wi, $"{tag}-player{wi}", null, 0, id, "stress");
                            d.PlayerSeen($"{tag}{wi:D2}{i:D4}", wi, $"{tag}-player{wi}-{i}", 5);
                            d.AddInventorySnapshot($"{tag}{wi:D2}", $"{tag}-player{wi}", "manual", "Akua",
                                $"[{{\"id\":{i},\"count\":{wi + 1},\"slot\":1}}]", $"[{{\"id\":4297,\"count\":{i},\"slot\":10}}]", DateTime.UtcNow);
                            d.AddJournal("stress", 1, id, $"{tag}{wi:D2}");
                            d.AddStat(new StatRow(DateTime.UtcNow, "pf:" + tag, 1, 2, wi, 1, 30, i));
                            d.SetSetting($"stress.{wi}", id);
                            d.AddPositions([new PositionRow(DateTime.UtcNow, "p", $"{tag}{wi:D2}", tag, "Akua", i, wi, 0)]);
                            if (i % 10 == 0) d.InTransaction((c, tx) =>
                            {
                                for (var k = 0; k < 25; k++)
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
                    }
                    catch (Exception e) { errors.Add($"{tag} writer {wi}: {e.Message}"); }
                }));
            }
            // Readers at the same time (the web panel reading while the game writes).
            threads.Add(new Thread(() =>
            {
                try
                {
                    for (var i = 0; i < 200; i++)
                    {
                        _ = d.RecentChat(200);
                        _ = d.Players();
                        _ = d.InventorySnapshots(null, 50);
                        _ = d.Journal("stress", 0, 50);
                    }
                }
                catch (Exception e) { errors.Add($"{tag} reader: {e.Message}"); }
            }));
        }
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        output.WriteLine($"{threads.Count} threads, {sw.ElapsedMilliseconds} ms");

        Assert.Empty(errors);
        foreach (var tag in tags)
        {
            var f = files[tag];
            var other = tag == "A" ? "B" : "A";
            Assert.Equal("ok", IntegrityCheck(f));
            // Every write is there, exactly once.
            Assert.Equal(writers * ops, Scalar(f, "SELECT COUNT(*) FROM chat WHERE source='stress';"));
            Assert.Equal(writers * (ops / 10) * 25, Scalar(f, "SELECT COUNT(*) FROM chat WHERE source='batch';"));
            Assert.Equal(writers * ops, Scalar(f, "SELECT COUNT(*) FROM players;"));
            Assert.Equal(writers * ops, Scalar(f, "SELECT COUNT(*) FROM inventory_snapshots;"));
            Assert.Equal(writers * ops, Scalar(f, "SELECT COUNT(*) FROM journal WHERE category='stress';"));
            Assert.Equal(writers * ops, Scalar(f, $"SELECT COUNT(*) FROM positions;"));
            Assert.Equal(writers * ops, Scalar(f, "SELECT COUNT(*) FROM chat WHERE source='stress' AND text LIKE '" + tag + "-%';"));
            // Nothing from the other server.
            Assert.Equal(0, Scalar(f, $"SELECT COUNT(*) FROM chat WHERE text LIKE '{other}-%' OR sender_name LIKE '{other}%';"));
            Assert.Equal(0, Scalar(f, $"SELECT COUNT(*) FROM players WHERE steam_id LIKE '{other}%' OR name LIKE '{other}-%';"));
            Assert.Equal(0, Scalar(f, $"SELECT COUNT(*) FROM inventory_snapshots WHERE steam_id LIKE '{other}%';"));
            Assert.Equal(0, Scalar(f, $"SELECT COUNT(*) FROM journal WHERE text LIKE '{other}-%';"));
            // Last value of each setting is a value written by this server.
            for (var w = 0; w < writers; w++) Assert.Equal($"{tag}-{w}-{ops - 1}", data[tag].Setting($"stress.{w}"));
            output.WriteLine($"{tag}: {new FileInfo(f).Length / 1024} KB, integrity ok");
        }
    }

    [Fact]
    public void WriterKilledInTheMiddleNeverCorruptsTheDatabase()
    {
        using var dir = new TempDir();
        string[] tags = ["A", "B"];
        var files = tags.ToDictionary(t => t, t => Path.Combine(dir.Path, t, "eam", "server.db"));
        var tool = Path.Combine(AppContext.BaseDirectory, "Eam.StressTool.dll");
        Assert.True(File.Exists(tool), tool);
        var dotnet = Environment.ProcessPath!.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase) || Environment.ProcessPath!.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            ? Environment.ProcessPath! : "dotnet";
        var rnd = new Random(42);
        long lastA = 0, lastB = 0;
        for (var round = 0; round < 6; round++)
        {
            // Both servers write at the same time, then both processes are killed without warning.
            var procs = tags.Select(t => Process.Start(new ProcessStartInfo(dotnet, $"\"{tool}\" \"{files[t]}\" {t}-{round}")
            { RedirectStandardOutput = true, UseShellExecute = false })!).ToList();
            foreach (var p in procs) Assert.Equal("ready", p.StandardOutput.ReadLine());
            Thread.Sleep(400 + rnd.Next(800));
            foreach (var p in procs) { p.Kill(entireProcessTree: true); p.WaitForExit(); p.Dispose(); }

            foreach (var t in tags)
            {
                Assert.Equal("ok", IntegrityCheck(files[t]));
                // The database still opens and works normally after the crash.
                var d = new ServerData(files[t]);
                d.AddChat(DateTime.UtcNow, "Global", 0, "check", null, 0, $"{t}-after-{round}", "check");
                var other = t == "A" ? "B" : "A";
                Assert.Equal(0, Scalar(files[t], $"SELECT COUNT(*) FROM chat WHERE text LIKE '{other}-%' OR sender_name LIKE '{other}%';"));
                // A batch is written in one transaction: after a crash it is either all there or not there at all.
                Assert.Equal(0, Scalar(files[t], "SELECT COUNT(*) FROM (SELECT substr(text, 1, instr(text, '-batch-') - 1) b, COUNT(*) n FROM chat WHERE source='batch' GROUP BY b HAVING n <> 20);"));
            }
            var a = Scalar(files["A"], "SELECT COUNT(*) FROM chat;");
            var b = Scalar(files["B"], "SELECT COUNT(*) FROM chat;");
            Assert.True(a > lastA && b > lastB, $"round {round}: nothing written ({a}, {b})");
            output.WriteLine($"round {round}: A {a} messages, B {b} messages, both intact");
            lastA = a; lastB = b;
        }
    }
}
