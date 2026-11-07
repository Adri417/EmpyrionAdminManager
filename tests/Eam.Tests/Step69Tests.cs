using Eam.Core.Servers;
using Eam.Engine.Servers;

namespace Eam.Tests;

public class WorldLogicTests
{
    private static StructureRow S(int id, int type, int fg, int fid, string pf = "Akua", int cls = 5, int blocks = 100, DateTime? visited = null, DateTime? first = null) =>
        new(id, "S" + id, type, pf, fg, fid, 0, 0, 0, blocks, 10, cls, 1, true, visited, "Alpha", first ?? DateTime.UtcNow, DateTime.UtcNow, null, null);

    [Fact]
    public void LimitCountFlagsNewestStructures()
    {
        var list = new[] { S(10, EntityTypes.BA, FactionGroups.Player, 1001), S(11, EntityTypes.BA, FactionGroups.Player, 1001), S(12, EntityTypes.BA, FactionGroups.Player, 1001),
            S(20, EntityTypes.BA, FactionGroups.Player, 1002) };
        var v = LimitChecker.Check(list, [new LimitRule { Scope = "player", Type = "BA", MaxCount = 2 }], (_, id) => "P" + id, (_, _) => false);
        var one = Assert.Single(v);
        Assert.Equal(12, one.StructureId);
        Assert.Equal("1:1001", one.OwnerKey);
    }

    [Fact]
    public void LimitClassAndPlayfieldAndExempt()
    {
        var list = new[] { S(1, EntityTypes.CV, FactionGroups.Faction, 7, "Akua", cls: 12), S(2, EntityTypes.CV, FactionGroups.Faction, 7, "Omicron", cls: 12),
            S(3, EntityTypes.CV, FactionGroups.Faction, 8, "Akua", cls: 12), S(4, EntityTypes.CV, FactionGroups.Zirax, 0, "Akua", cls: 50) };
        var v = LimitChecker.Check(list, [new LimitRule { Scope = "faction", Type = "CV", Playfields = "Ak*", MaxClass = 10 }], (_, id) => "F" + id, (_, id) => id == 8);
        Assert.Equal([1], v.Select(x => x.StructureId));
    }

    [Fact]
    public void AutoWipeNotVisitedAndAge()
    {
        var now = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        var list = new[] { S(1, EntityTypes.BA, 1, 1, visited: now.AddDays(-40)), S(2, EntityTypes.BA, 1, 1, visited: now.AddDays(-2)),
            S(3, EntityTypes.SV, 1, 1, "Start", first: now.AddHours(-30)), S(4, EntityTypes.SV, 1, 1, "Start", first: now.AddHours(-2)) };
        var r = LimitChecker.AutoWipe(list, [new AutoWipeRule { NotVisitedDays = 30 }, new AutoWipeRule { Playfields = "Start", OlderThanHours = 24 }], now, (_, _) => false);
        Assert.Equal([1, 3], r.Select(x => x.Structure.Id));
    }

    [Theory]
    [InlineData("", "Akua", true)]
    [InlineData("Akua, Omicron", "omicron", true)]
    [InlineData("Start*", "Starter Planet", true)]
    [InlineData("Akua", "Akua Orbit", false)]
    public void PlayfieldPatterns(string patterns, string pf, bool expected) => Assert.Equal(expected, FeatureStore.MatchesPlayfield(patterns, pf));

    [Fact]
    public void SharedCleanupKeepsLiveAndRecentFolders()
    {
        using var dir = new TempDir();
        foreach (var n in new[] { "100", "101", "102", "abc" })
        {
            var d = Directory.CreateDirectory(Path.Combine(dir.Path, n));
            var f = Path.Combine(d.FullName, "x.bin");
            File.WriteAllBytes(f, new byte[10]);
            File.SetLastWriteTimeUtc(f, n == "102" ? DateTime.UtcNow : DateTime.UtcNow.AddDays(-5));
        }
        var old = SharedCleanup.Obsolete(dir.Path, new HashSet<int> { 100 }, TimeSpan.FromDays(1));
        var one = Assert.Single(old);
        Assert.Equal("101", one.Name);
        Assert.Equal(10, one.Size);
    }

    [Fact]
    public void SyncStructuresWritesEvents()
    {
        using var dir = new TempDir();
        var data = new ServerData(Path.Combine(dir.Path, "eam", "server.db"));
        var t0 = DateTime.UtcNow;
        data.SyncStructures([S(1, EntityTypes.BA, 1, 1001), S(2, EntityTypes.CV, 1, 1001)], t0);
        Assert.Equal(2, data.Structures().Count);
        var ev = data.SyncStructures([S(1, EntityTypes.BA, 1, 1002) with { Name = "Renamed" }, S(3, EntityTypes.SV, 1, 1001)], t0.AddMinutes(5));
        var kinds = ev.Select(e => (e.StructureId, e.Kind)).ToHashSet();
        Assert.Contains((1, "renamed"), kinds);
        Assert.Contains((1, "owner"), kinds);
        Assert.Contains((2, "destroyed"), kinds);
        Assert.Contains((3, "created"), kinds);
        Assert.Equal([1, 3], data.Structures().Select(s => s.Id).OrderBy(x => x));
        Assert.NotEmpty(data.StructureEvents(1));
    }
}

public class CommunityLogicTests
{
    [Theory]
    [InlineData("T'es un CONNARD", "connard")]
    [InlineData("cönnard!", "connard")]
    [InlineData("déconnardise", null)]
    [InlineData("bonjour", null)]
    public void BadWords(string text, string? expected) => Assert.Equal(expected, CommunityService.FindBadWord(text, ["connard", "  "]));

    [Fact]
    public void PlainRemovesAccents() => Assert.Equal("eeeacou", CommunityService.Plain("ÉÈÊÀÇÔÛ"));

    [Fact]
    public void CountsFloodText() => Assert.Equal(3, SecurityService.CountOccurrences("aXXbXXcXX", "XX"));
}

public class GameplayLogicTests
{
    private static readonly List<OreConfig> Ores = [new() { Key = "iron", ItemId = 4296, PerHour = 10, Price = 1000 }];

    [Fact]
    public void AccrueUsesFuelAndCapsStorage()
    {
        var t0 = new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc);
        var miners = new[] { new MinerRow("s", "iron", 2, 0, 0, t0) };
        // 5 hours passed, only 3 hours of fuel: 3 h × 10 × level 2 = 60.
        var (m, fuel) = GameplayService.Accrue(miners, Ores, 3, t0, t0.AddHours(5), 48);
        Assert.Equal(60, m[0].Stored);
        Assert.Equal(0, fuel);
        // Storage is capped at maxStoredHours of production (4 h × 20 = 80).
        var (m2, fuel2) = GameplayService.Accrue(miners, Ores, 100, t0, t0.AddHours(10), 4);
        Assert.Equal(80, m2[0].Stored);
        Assert.Equal(90, fuel2);
    }

    [Fact]
    public void SettingsValidation()
    {
        var s = new GameplaySettings { Ores = [new() { Key = "iron", ItemId = 1, PerHour = 1 }, new() { Key = "IRON", ItemId = 2, PerHour = 1 }] };
        Assert.Equal("invalid_ore", Assert.Throws<FormatException>(s.Validate).Message);
        new GameplaySettings { Ores = Ores }.Validate();
    }
}
