using System.Collections.Concurrent;

namespace webBasicCWFixer.Api.Jobs;

public sealed class JobStore
{
    private readonly ConcurrentDictionary<string, JobState> _jobs = new();

    public JobState Create(string jobId) =>
        _jobs[jobId] = new JobState { JobId = jobId };

    public bool TryGet(string jobId, out JobState? job) => _jobs.TryGetValue(jobId, out job);

    public void Remove(string jobId) => _jobs.TryRemove(jobId, out _);
}
