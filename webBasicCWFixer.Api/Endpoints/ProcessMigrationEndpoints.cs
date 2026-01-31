using System.Text;
using webBasicCWFixer.Analyzer.ProcessMigration;
using webBasicCWFixer.Api.Jobs;

namespace webBasicCWFixer.Api.Endpoints;

public static class ProcessMigrationEndpoints
{
    public static IEndpointRouteBuilder MapProcessMigrationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/jobs/{jobId}/migrations", (string jobId, int? limit, JobStore store) =>
        {
            if (!store.TryGet(jobId, out var job) || job is null)
            {
                return Results.NotFound();
            }

            if (!job.MigrationCompleted)
            {
                return Results.Accepted(value: new { message = "Migration analiz tamamlanmadı." });
            }

            if (string.IsNullOrWhiteSpace(job.MigrationOutputPath) || !File.Exists(job.MigrationOutputPath))
            {
                return Results.NotFound("Migration çıktısı bulunamadı.");
            }

            var max = limit is > 0 ? Math.Min(500, limit.Value) : 200;
            var items = new List<ProcessMigrationFinding>();
            int total = 0;

            using var fs = File.OpenRead(job.MigrationOutputPath);
            using var reader = new StreamReader(fs, Encoding.UTF8, true, bufferSize: 1024 * 32);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;
                total++;
                if (items.Count >= max) continue;

                var finding = System.Text.Json.JsonSerializer.Deserialize<ProcessMigrationFinding>(line);
                if (finding is not null)
                {
                    items.Add(finding);
                }
            }

            return Results.Ok(new { total, items });
        });

        return app;
    }
}
