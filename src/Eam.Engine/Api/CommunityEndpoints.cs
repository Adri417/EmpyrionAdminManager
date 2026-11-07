using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Eam.Core.Security;
using Eam.Engine.Servers;

namespace Eam.Engine.Api;

public sealed record MuteRequest(string SteamId, int Minutes);
public sealed record SteamRequest(string SteamId);

/// <summary>Step 7: moderation, Discord test and translation of chat messages.</summary>
public static class CommunityEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/servers/{id:long}");
        static string By(HttpContext c) => c.User.Identity?.Name ?? "?";

        g.MapGet("/moderation", (long id, CommunityService s) => Api.Run(() => s.Moderation(id))).RequireAuthorization(Roles.Moderator);
        g.MapPost("/moderation/mute", (long id, MuteRequest req, CommunityService s, HttpContext c) =>
            Api.Run(() => { s.Mute(id, req.SteamId, req.Minutes, By(c)); return Api.Ok; })).RequireAuthorization(Roles.Moderator);
        g.MapPost("/moderation/reset", (long id, SteamRequest req, CommunityService s, HttpContext c) =>
            Api.Run(() => { s.ResetWarnings(id, req.SteamId, By(c)); return Api.Ok; })).RequireAuthorization(Roles.Moderator);
        g.MapPost("/discord/test", (long id, DiscordService d) => Api.RunAsync(async () => { await d.TestAsync(id); return Api.Ok; }))
            .RequireAuthorization(Roles.Admin);
        g.MapGet("/discord/status", (long id, DiscordService d) => Results.Ok(new { status = d.Status(id) })).RequireAuthorization(Roles.Admin);

        // Google's free translation endpoint, called by the engine so the page keeps its strict security policy.
        app.MapGet("/api/translate", (string text, string? to, IHttpClientFactory http) => Api.RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 1000) throw new ServerException("invalid_value");
            var lang = string.IsNullOrWhiteSpace(to) ? "fr" : to.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(lang, "^[a-z]{2}(-[A-Za-z]{2})?$")) throw new ServerException("invalid_language");
            using var c = http.CreateClient("web");
            var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={lang}&dt=t&q={Uri.EscapeDataString(text)}";
            var json = await c.GetFromJsonAsync<JsonElement>(url);
            var sb = new StringBuilder();
            foreach (var part in json[0].EnumerateArray()) sb.Append(part[0].GetString());
            var from = json.GetArrayLength() > 2 && json[2].ValueKind == JsonValueKind.String ? json[2].GetString() : null;
            return new { text = sb.ToString(), from };
        })).RequireAuthorization(Roles.Viewer);
    }
}
