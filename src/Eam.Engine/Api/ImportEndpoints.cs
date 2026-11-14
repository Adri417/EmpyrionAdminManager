using Eam.Core.Security;
using Eam.Engine.Servers;

namespace Eam.Engine.Api;

/// <summary>Import of a server that used EAH (or any existing Empyrion server).</summary>
public static class ImportEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/import/preview", (ImportPreviewRequest req, ImportService s, ILoggerFactory lf) => Run(() => Task.FromResult(s.Preview(req.Path)), lf, "analyse " + req.Path))
            .RequireAuthorization(Roles.Owner);
        app.MapPost("/api/import", (ImportRequest req, ImportService s, HttpContext c, ILoggerFactory lf) => Run(() => s.ImportAsync(req, c.User.Identity?.Name ?? "?"), lf, $"import ({req.Mode}) {req.Path}"))
            .RequireAuthorization(Roles.Owner);
    }

    /// <summary>Every refusal is also written in EAM's log (the reason is not only shown on screen).</summary>
    private static async Task<IResult> Run(Func<Task<object>> action, ILoggerFactory lf, string what)
    {
        var log = lf.CreateLogger("Eam.Import");
        try { return Results.Ok(await action()); }
        catch (ServerException e)
        {
            log.LogWarning("Import refused, {What}: {Code} {Detail}", what, e.Code, e.Detail);
            return Results.BadRequest(new { error = e.Code, detail = e.Detail });
        }
        catch (Exception e) when (e is ArgumentException or FormatException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log.LogWarning("Import failed, {What}: {Type}: {Err}", what, e.GetType().Name, e.Message);
            return Results.BadRequest(new { error = "io_error", detail = e.Message });
        }
        catch (Exception e)
        {
            log.LogError(e, "Import crashed, {What}", what);
            throw;
        }
    }
}
