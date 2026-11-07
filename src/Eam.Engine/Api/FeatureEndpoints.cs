using System.Text.Json;
using Eam.Core.Security;
using Eam.Core.Servers;
using Eam.Engine.Servers;

namespace Eam.Engine.Api;

/// <summary>Settings of the features of steps 6 to 9, one JSON document per feature and per server.</summary>
public static class FeatureEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/servers/{id:long}/features");

        g.MapGet("/{key}", (long id, string key, ServerManager m, LiveService live) => Api.Run(() =>
        {
            var data = live.Data(m.Definition(id));
            return key switch
            {
                "world" => FeatureStore.Load<WorldSettings>(data, key),
                "community" => Masked(FeatureStore.Load<CommunitySettings>(data, key)),
                "security" => FeatureStore.Load<SecuritySettings>(data, key),
                "gameplay" => FeatureStore.Load<GameplaySettings>(data, key),
                _ => throw new ServerException("not_found"),
            };
        })).RequireAuthorization(Roles.Admin);

        g.MapPut("/{key}", async (long id, string key, HttpRequest req, ServerManager m, LiveService live, HttpContext c) =>
        {
            JsonElement body;
            try { body = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body); }
            catch (JsonException) { return Results.BadRequest(new { error = "invalid_json" }); }
            return Api.Run(() =>
            {
                var def = m.Definition(id);
                var data = live.Data(def);
                switch (key)
                {
                    case "world": FeatureStore.Save(data, key, Parse<WorldSettings>(body)); break;
                    case "community":
                    {
                        var next = Parse<CommunitySettings>(body);
                        // An empty token keeps the stored one: the interface never receives it.
                        if (next.DiscordToken.Length == 0) next.DiscordToken = FeatureStore.Load<CommunitySettings>(data, key).DiscordToken;
                        FeatureStore.Save(data, key, next);
                        break;
                    }
                    case "security": FeatureStore.Save(data, key, Parse<SecuritySettings>(body)); break;
                    case "gameplay": FeatureStore.Save(data, key, Parse<GameplaySettings>(body)); break;
                    default: throw new ServerException("not_found");
                }
                ServerManager.ServerLog(def, $"{c.User.Identity?.Name}: {key} settings changed");
                FeaturesChanged?.Invoke(id, key);
                return Api.Ok;
            });
        }).RequireAuthorization(Roles.Admin);
    }

    /// <summary>Lets services reload settings they keep in memory (Discord connection).</summary>
    public static event Action<long, string>? FeaturesChanged;

    private static T Parse<T>(JsonElement body) where T : new()
    {
        try { return body.Deserialize<T>(FeatureStore.Json) ?? new T(); }
        catch (JsonException) { throw new FormatException("invalid_json"); }
    }

    private static object Masked(CommunitySettings s)
    {
        var hasToken = s.DiscordToken.Length > 0;
        s.DiscordToken = "";
        var json = JsonSerializer.SerializeToElement(s, FeatureStore.Json);
        var dict = json.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value);
        dict["hasDiscordToken"] = hasToken;
        return dict;
    }
}
