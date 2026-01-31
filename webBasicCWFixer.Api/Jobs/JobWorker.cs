using webBasicCWFixer.Analyzer;
using webBasicCWFixer.Api.Allowlist;
using webBasicCWFixer.Analyzer.ProcessMigration;

namespace webBasicCWFixer.Api.Jobs;

public sealed class JobWorker : BackgroundService
{
    private readonly JobQueue _queue;
    private readonly JobStore _store;
    private readonly AllowlistService _allowlist;
    private readonly AnalyzerService _analyzer;
    private readonly ProcessMigrationAnalyzer _migrationAnalyzer;
    private readonly ILogger<JobWorker> _log;

    public JobWorker(
        JobQueue queue,
        JobStore store,
        AllowlistService allowlist,
        AnalyzerService analyzer,
        ProcessMigrationAnalyzer migrationAnalyzer,
        ILogger<JobWorker> log)
    {
        _queue = queue;
        _store = store;
        _allowlist = allowlist;
        _analyzer = analyzer;
        _migrationAnalyzer = migrationAnalyzer;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var jobId = await _queue.DequeueAsync(stoppingToken);

            if (!_store.TryGet(jobId, out var job) || job is null) continue;

            try
            {
                job.Status = JobStatus.Running;
                job.Progress = 5;
                job.Message = "Analiz başlıyor...";

                var cfg = _allowlist.Load();

                var logPath = Path.Combine(Path.GetTempPath(), $"webBasicCWFixer_{jobId}.log");
                job.LogPath = logPath;

                // kaba progress (scriptCount üzerinden)
                int lastReported = 0;
                var result = _analyzer.AnalyzeFile(job.XmlPath!, cfg, logPath, onProgress: sc =>
                {
                    // sc scriptCount; burada yüzdeyi kaba hesaplayamıyoruz çünkü total bilmiyoruz.
                    // O yüzden sadece “çalışıyor” hissi için 5-95 arası yürütelim.
                    if (sc - lastReported >= 50)
                    {
                        lastReported = sc;
                        job.Progress = Math.Min(95, job.Progress + 2);
                        job.Message = $"Script taranıyor... ({sc})";
                    }
                });

                job.ScriptCount = result.ScriptCount;
                job.IssueCount = result.IssueCount;
                job.Issues.AddRange(result.Issues);

                job.Message = "Migration kontrolü...";
                var migrationOutput = Path.Combine(Path.GetTempPath(), $"webBasicCWFixer_{jobId}_migrations.jsonl");
                job.MigrationOutputPath = migrationOutput;
                var migrationSummary = _migrationAnalyzer.Analyze(
                    job.XmlPath!,
                    migrationOutput,
                    OutputFormat.Jsonl,
                    debug: false);
                job.MigrationFindingCount = migrationSummary.FindingCount;
                job.MigrationCompleted = true;

                job.Progress = 100;
                job.Message = "Bitti";
                job.Status = JobStatus.Done;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Job failed {JobId}", jobId);
                job.Status = JobStatus.Error;
                job.Error = ex.Message;
                job.Message = "Hata";
                job.MigrationError ??= ex.Message;
            }
            finally
            {
                // input temp dosyayı sil
                TryDelete(job.XmlPath);
            }
        }
    }

    private static void TryDelete(string? path)
    {
        try { if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); }
        catch { /* ignore */ }
    }
}
