using System.Collections.Concurrent;
using System.Threading.Channels;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>Two bounded ephemeral queues: embeddings and review-only completion proposals.</summary>
public sealed class KnowledgeBackgroundQueue(KnowledgeBackgroundOptions options) : IKnowledgeBackgroundQueue
{
    private readonly Channel<KnowledgeBackgroundWorkItem> _embedding = Channel.CreateBounded<KnowledgeBackgroundWorkItem>(
        new BoundedChannelOptions(options.EmbeddingQueueCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly Channel<KnowledgeBackgroundWorkItem> _proposals = Channel.CreateBounded<KnowledgeBackgroundWorkItem>(
        new BoundedChannelOptions(options.ProposalQueueCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly ConcurrentDictionary<string, KnowledgeBackgroundJobSnapshot> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, KnowledgeProposalSet> _results = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _retention = new();
    private readonly object _state = new();

    public Task<KnowledgeBackgroundJobSnapshot> EnqueueAsync(
        KnowledgeBackgroundEnqueueRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Valid(request)) throw new ArgumentException("The background request is malformed.", nameof(request));
        var id = $"knowledge-job.{Guid.NewGuid():n}";
        var work = new KnowledgeBackgroundWorkItem(
            id,
            request.Kind,
            request.WorldId,
            (request.KnowledgeIds ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        var snapshot = new KnowledgeBackgroundJobSnapshot(
            id, request.Kind, request.WorldId, "queued", 0, DateTimeOffset.UtcNow);
        lock (_state)
        {
            // Publish job state before making work visible to the single reader.
            _jobs[id] = snapshot;
            _cancellations[id] = new CancellationTokenSource();
            _retention.Enqueue(id);
            if (!Writer(request.Kind).TryWrite(work))
            {
                snapshot = snapshot with
                {
                    Status = "rejected",
                    CompletedAt = DateTimeOffset.UtcNow,
                    ErrorCode = "BACKGROUND_QUEUE_FULL",
                    ErrorMessage = "The bounded background queue is full."
                };
                _jobs[id] = snapshot;
            }
            Prune();
        }
        return Task.FromResult(snapshot);
    }

    public KnowledgeBackgroundJobSnapshot? Get(string jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job : null;

    public KnowledgeProposalSet? GetProposal(string jobId) =>
        _results.TryGetValue(jobId, out var result) ? result : null;

    public bool Cancel(string jobId)
    {
        lock (_state)
        {
            if (!_jobs.TryGetValue(jobId, out var job) || job.Status is
                    "completed" or "failed" or "fallback" or "stale" or "rejected" or "cancelled")
                return false;
            if (_cancellations.TryGetValue(jobId, out var cancellation)) cancellation.Cancel();
            _jobs[jobId] = job with
            {
                Status = "cancelled",
                CompletedAt = DateTimeOffset.UtcNow,
                ErrorCode = "BACKGROUND_CANCELLED",
                ErrorMessage = "The background job was cancelled."
            };
            return true;
        }
    }

    public CancellationToken Cancellation(string jobId) =>
        _cancellations.TryGetValue(jobId, out var cancellation)
            ? cancellation.Token
            : new CancellationToken(canceled: true);

    public IAsyncEnumerable<KnowledgeBackgroundWorkItem> ReadAllAsync(
        KnowledgeBackgroundJobKind kind,
        CancellationToken cancellationToken) =>
        Reader(kind).ReadAllAsync(cancellationToken);

    public KnowledgeBackgroundJobSnapshot? MarkRunning(KnowledgeBackgroundWorkItem work)
    {
        lock (_state)
        {
            if (!_jobs.TryGetValue(work.JobId, out var current) || current.Status == "cancelled") return null;
            var updated = current with
            {
                Status = "running",
                Attempt = current.Attempt + 1,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = null,
                ErrorCode = "",
                ErrorMessage = ""
            };
            _jobs[work.JobId] = updated;
            return updated;
        }
    }

    public bool Requeue(KnowledgeBackgroundWorkItem work)
    {
        lock (_state)
        {
            if (!_jobs.TryGetValue(work.JobId, out var current) || current.Status == "cancelled") return false;
            var written = Writer(work.Kind).TryWrite(work);
            _jobs[work.JobId] = written
                ? current with { Status = "queued", StartedAt = null }
                : current with
                {
                    Status = "failed",
                    CompletedAt = DateTimeOffset.UtcNow,
                    ErrorCode = "BACKGROUND_QUEUE_FULL",
                    ErrorMessage = "The retry could not re-enter the bounded queue."
                };
            return written;
        }
    }

    public void Complete(KnowledgeBackgroundWorkItem work, KnowledgeBackgroundOutcome outcome)
    {
        lock (_state)
        {
            if (!_jobs.TryGetValue(work.JobId, out var current))
                throw new InvalidOperationException("Unknown background job.");
            if (current.Status == "cancelled") return;
            if (outcome.Proposal is not null) _results[work.JobId] = outcome.Proposal;
            _jobs[work.JobId] = current with
            {
                Status = outcome.Status,
                CompletedAt = DateTimeOffset.UtcNow,
                Model = outcome.Model,
                ModelRevision = outcome.ModelRevision,
                ModelProfile = outcome.ModelProfile,
                InputFingerprint = outcome.InputFingerprint,
                SafeSummary = outcome.SafeSummary,
                FallbackCode = outcome.FallbackCode,
                ErrorCode = outcome.ErrorCode,
                ErrorMessage = outcome.ErrorMessage
            };
        }
    }

    private ChannelWriter<KnowledgeBackgroundWorkItem> Writer(KnowledgeBackgroundJobKind kind) =>
        kind == KnowledgeBackgroundJobKind.EmbeddingSync ? _embedding.Writer : _proposals.Writer;
    private ChannelReader<KnowledgeBackgroundWorkItem> Reader(KnowledgeBackgroundJobKind kind) =>
        kind == KnowledgeBackgroundJobKind.EmbeddingSync ? _embedding.Reader : _proposals.Reader;

    private void Prune()
    {
        while (_jobs.Count > options.MaxRetainedJobs && _retention.TryDequeue(out var id))
        {
            if (!_jobs.TryGetValue(id, out var job)) continue;
            if (job.Status is "queued" or "running") { _retention.Enqueue(id); break; }
            _jobs.TryRemove(id, out _);
            _results.TryRemove(id, out _);
            if (_cancellations.TryRemove(id, out var cancellation)) cancellation.Dispose();
        }
    }

    private static bool Valid(KnowledgeBackgroundEnqueueRequest? request)
    {
        if (request is null || !Id(request.WorldId)) return false;
        var ids = request.KnowledgeIds ?? [];
        if (ids.Count > 8 || ids.Any(id => !Id(id)) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            return false;
        return request.Kind switch
        {
            KnowledgeBackgroundJobKind.EmbeddingSync => ids.Count == 0,
            KnowledgeBackgroundJobKind.KnowledgeProposals => ids.Count is >= 1 and <= 8,
            _ => false
        };
    }

    private static bool Id(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= 200;
}

public sealed record KnowledgeBackgroundWorkItem(
    string JobId,
    KnowledgeBackgroundJobKind Kind,
    string WorldId,
    IReadOnlyList<string> KnowledgeIds);

public sealed record KnowledgeBackgroundOutcome(
    string Status,
    bool Retryable,
    string Model = "",
    string ModelRevision = "",
    string InputFingerprint = "",
    string SafeSummary = "",
    string FallbackCode = "",
    string ErrorCode = "",
    string ErrorMessage = "",
    KnowledgeProposalSet? Proposal = null,
    string ModelProfile = "");
