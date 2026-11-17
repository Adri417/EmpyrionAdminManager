using Eam.Core.Servers;
using Xunit;

namespace Eam.Tests;

public class WorldCompactTests
{
    [Fact]
    public void Merges_rules_that_only_differ_by_playfield()
    {
        var w = new WorldSettings();
        for (var i = 0; i < 3; i++)
            foreach (var pf in new[] { "Akua", "Omicron", "Akua" })
                w.Limits.Add(new LimitRule { Scope = "player", Type = "CV", MaxCount = 2, Playfields = pf });
        w.Limits.Add(new LimitRule { Scope = "faction", Type = "BA", MaxCount = 5, Playfields = "Akua" });
        w.AutoWipe.Add(new AutoWipeRule { Playfields = "Start1", OlderThanHours = 24 });
        w.AutoWipe.Add(new AutoWipeRule { Playfields = "Start2", OlderThanHours = 24 });

        Assert.True(w.Compact());
        Assert.Equal(2, w.Limits.Count);
        Assert.Equal("Akua, Omicron", w.Limits[0].Playfields);
        Assert.Single(w.AutoWipe);
        Assert.Equal("Start1, Start2", w.AutoWipe[0].Playfields);
        Assert.False(w.Compact());
        Assert.True(FeatureStore.MatchesPlayfield(w.Limits[0].Playfields, "omicron"));
        Assert.False(FeatureStore.MatchesPlayfield(w.Limits[0].Playfields, "Ningues"));
    }

    [Fact]
    public void Empty_playfield_list_means_everywhere_and_wins()
    {
        var w = new WorldSettings();
        w.Limits.Add(new LimitRule { Scope = "player", Type = "SV", MaxCount = 3, Playfields = "Akua" });
        w.Limits.Add(new LimitRule { Scope = "player", Type = "SV", MaxCount = 3, Playfields = "" });
        Assert.True(w.Compact());
        Assert.Equal("", Assert.Single(w.Limits).Playfields);
    }
}
