using Eam.Engine.Infrastructure;
using Xunit;

namespace Eam.Tests;

public class UpdateTests
{
    [Theory]
    [InlineData("v0.9.5", "0.9.0", true)]
    [InlineData("0.9.5", "0.9.5", false)]
    [InlineData("v0.9.4", "0.9.5", false)]
    [InlineData("v1.0.0", "0.9.5", true)]
    [InlineData("v0.10.0", "0.9.5", true)]
    [InlineData("nonsense", "0.9.5", false)]
    [InlineData(null, "0.9.5", false)]
    public void ComparesVersions(string? latest, string current, bool newer) => Assert.Equal(newer, UpdateService.IsNewer(latest, current));

    [Fact]
    public void ScriptNeverCopiesTheDataFolder()
    {
        var s = UpdateService.Script(@"C:\t\new\EAM", @"C:\EAM", @"C:\EAM\data\temp\update\previous", @"C:\EAM\data\logs\update.log", 1234, false);
        Assert.Contains("/XD (Join-Path $src 'data')", s);
        Assert.Contains("/XD (Join-Path $dst 'data')", s);
        Assert.Contains("/XF portable.txt", s);
        Assert.DoesNotContain("/MIR", s);
        Assert.Contains("Get-Process -Id 1234", s);
    }
}
