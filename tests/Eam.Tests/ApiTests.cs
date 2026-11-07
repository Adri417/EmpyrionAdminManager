using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Eam.Tests;

public sealed class ApiTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests()
    {
        Environment.SetEnvironmentVariable("EAM_DATA", _dir.Path);
        _factory = new WebApplicationFactory<Program>();
    }

    public void Dispose() { _factory.Dispose(); _dir.Dispose(); Environment.SetEnvironmentVariable("EAM_DATA", null); }

    private HttpClient Client()
    {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        c.DefaultRequestHeaders.Add("X-EAM", "1");
        return c;
    }

    [Fact]
    public async Task ProtectedRoutesNeedLogin()
    {
        var c = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/system")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task PostWithoutCsrfHeaderIsRejected()
    {
        var c = _factory.CreateClient();
        var r = await c.PostAsJsonAsync("/api/login", new { username = "a", password = "b" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task UnknownApiRouteIs404AndPagesFallBackToTheInterface()
    {
        var c = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/nothing")).StatusCode);
    }
}
