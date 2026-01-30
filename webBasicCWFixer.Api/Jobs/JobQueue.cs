using System.Threading.Channels;

namespace webBasicCWFixer.Api.Jobs;

public sealed class JobQueue
{
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();

    public ValueTask EnqueueAsync(string jobId) => _queue.Writer.WriteAsync(jobId);
    public ValueTask<string> DequeueAsync(CancellationToken ct) => _queue.Reader.ReadAsync(ct);
}
