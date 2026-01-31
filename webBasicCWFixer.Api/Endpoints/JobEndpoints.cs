using webBasicCWFixer.Api.Jobs;

namespace webBasicCWFixer.Api.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/jobs/{jobId}", (string jobId, JobStore store) =>
        {
            if (!store.TryGet(jobId, out var job) || job is null)
                return Results.NotFound();

            return Results.Ok(new
            {
                job.JobId,
                status = job.Status.ToString(),
                job.Progress,
                job.Message,
                job.ScriptCount,
                job.IssueCount,
                hasLog = !string.IsNullOrWhiteSpace(job.LogPath) && File.Exists(job.LogPath),
                error = job.Error
            });
        });

        app.MapGet("/api/jobs/{jobId}/issues", (string jobId, int page, int pageSize, JobStore store) =>
        {
            page = page <= 0 ? 1 : page;
            pageSize = pageSize is <= 0 or > 500 ? 100 : pageSize;

            if (!store.TryGet(jobId, out var job) || job is null)
                return Results.NotFound();

            var total = job.Issues.Count;
            var items = job.Issues
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Results.Ok(new { total, page, pageSize, items });
        });

        app.MapGet("/api/jobs/{jobId}/log", (string jobId, JobStore store) =>
        {
            if (!store.TryGet(jobId, out var job) || job is null)
                return Results.NotFound();

            if (string.IsNullOrWhiteSpace(job.LogPath) || !File.Exists(job.LogPath))
                return Results.NotFound("Log yok.");

            return Results.File(job.LogPath, "text/plain", $"webBasicCWFixer_{jobId}.log");
        });

        return app;
    }
}
