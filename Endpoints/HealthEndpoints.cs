using CharleyCompany.Dashboard.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CharleyCompany.Dashboard.Web.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", async (ApplicationDbContext dbContext, CancellationToken cancellationToken) =>
        {
            try
            {
                var databaseReachable = await dbContext.Database.CanConnectAsync(cancellationToken);
                return databaseReachable
                    ? Results.Ok(new { status = "healthy", database = "reachable" })
                    : Results.Json(new { status = "unhealthy", database = "unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch
            {
                return Results.Json(new { status = "unhealthy", database = "unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).AllowAnonymous();

        return endpoints;
    }
}
