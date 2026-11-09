using CharleyCompany.Dashboard.Web.Data;
using CharleyCompany.Dashboard.Web.Services;

namespace CharleyCompany.Dashboard.Web.Endpoints;

public static class DeckPreviewEndpoints
{
    public static void MapDeckPreviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/deck-previews/{quoteId:int}/{versionId:int}/{taskId:int}/{optionId:guid}/{id:guid}/{kind}",
            (HttpContext http, DeckPreviewService service, int quoteId, int versionId, int taskId, Guid optionId, Guid id, string kind) =>
            {
                http.Response.Headers.CacheControl = "private, no-store";
                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                return kind is "original" or "result" or "download"
                    ? service.ImageAsync(http.User, new(quoteId, versionId, taskId, optionId), id,
                        original: kind == "original", download: kind == "download")
                    : Task.FromResult(Results.NotFound());
            }).RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Administrator, ApplicationRoles.LocalOperator));
    }
}
