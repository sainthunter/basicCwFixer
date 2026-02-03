namespace webBasicCWFixer.Api.Jobs;

public enum JobStatus { Queued, Running, Done, Error }

public sealed class JobState
{
    public required string JobId { get; init; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public int Progress { get; set; } = 0; // 0-100
    public string? Message { get; set; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    public int ScriptCount { get; set; }
    public int IssueCount { get; set; }
    public int WarningCount { get; set; }

    // UI için paging
    public List<webBasicCWFixer.Analyzer.IssueDto> Issues { get; } = new();
    public List<webBasicCWFixer.Analyzer.WarningDto> Warnings { get; } = new();

    // log path (temp)
    public string? LogPath { get; set; }

    // input temp xml path
    public string? XmlPath { get; set; }

    // process migration output
    public string? MigrationOutputPath { get; set; }
    public int MigrationFindingCount { get; set; }
    public bool MigrationCompleted { get; set; }
    public string? MigrationError { get; set; }

    public string? Error { get; set; }
}
