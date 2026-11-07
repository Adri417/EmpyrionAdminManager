using Eam.Core.Security;
using Eam.Engine.Servers;

namespace Eam.Engine.Api;

/// <summary>Import of a server that used EAH (or any existing Empyrion server).</summary>
public static class ImportEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/import/preview", (ImportPreviewRequest req, ImportService s) => Run(() => Task.FromResult(s.Preview(req.Path))))
            .RequireAuthorization(Roles.Owner);
        app.MapPost("/api/import", (ImportRequest req, ImportService s, HttpContext c) => Run(() => s.ImportAsync(req, c.User.Identity?.Name ?? "?")))
            .RequireAuthorization(Roles.Owner);
    }

    private static async Task<IResult> Run(Func<Task<object>> action)
    {
        try { return Results.Ok(await action()); }
        catch (ServerException e) { return Results.BadRequest(new { error = e.Code, detail = e.Detail }); }
        catch (Exception e) when (e is ArgumentException or FormatException or IOException or UnauthorizedAccessException or InvalidDataException)
        { return Results.BadRequest(new { error = "io_error", detail = e.Message }); }
    }
}
