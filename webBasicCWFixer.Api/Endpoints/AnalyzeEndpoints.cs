using Microsoft.AspNetCore.Http.Features;
using webBasicCWFixer.Api.Allowlist;
using webBasicCWFixer.Api.Jobs;

namespace webBasicCWFixer.Api.Endpoints;

public static class AnalyzeEndpoints
{
    public static IEndpointRouteBuilder MapAnalyzeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/analyze", async (
            IFormFile file,
            JobStore store,
            JobQueue queue,
            AllowlistService allowlist) =>
        {
            if (file == null || file.Length == 0)
                return Results.BadRequest("Dosya boş.");

            var cfg = allowlist.Load();
            var maxBytes = cfg.MaxUploadMb * 1024L * 1024L;

            if (file.Length > maxBytes)
                return Results.BadRequest($"Dosya çok büyük. Max {cfg.MaxUploadMb}MB.");

            var jobId = Guid.NewGuid().ToString("N");
            var job = store.Create(jobId);

            var tempXml = Path.Combine(Path.GetTempPath(), $"webBasicCWFixer_{jobId}.xml");
            job.XmlPath = tempXml;
            job.Message = "Upload alındı";
            job.Progress = 1;

            await using (var fs = File.Create(tempXml))
                await file.CopyToAsync(fs);

            job.Message = "Kuyruğa alındı";
            job.Progress = 3;

            await queue.EnqueueAsync(jobId);

            return Results.Ok(new { jobId });
        })
        .Accepts<IFormFile>("multipart/form-data")
        .DisableAntiforgery();

        return app;
    }
}
