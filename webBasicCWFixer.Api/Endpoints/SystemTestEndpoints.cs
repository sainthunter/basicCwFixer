using webBasicCWFixer.Api.SystemTests;

namespace webBasicCWFixer.Api.Endpoints;

public static class SystemTestEndpoints
{
    public static IEndpointRouteBuilder MapSystemTestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/system-test", async (
            SystemTestRunner runner,
            CancellationToken cancellationToken) =>
        {
            var result = await runner.RunAsync(cancellationToken);
            return Results.Ok(result);
        })
        .DisableAntiforgery();

        return app;
    }
}
