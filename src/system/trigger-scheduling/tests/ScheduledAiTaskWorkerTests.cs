using System.Collections.Concurrent;
using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;
using DantesRoleplay.SystemCapabilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class ScheduledAiTaskWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Slow_tasks_run_in_parallel_with_a_hard_concurrency_bound()
    {
        var time = new ManualTimeProvider(Now);
        var agent = new BlockingAgent(ScheduledAiTaskWorker.MaximumConcurrency);
        await using var harness = await Harness.CreateAsync(time, agent);
        for (var index = 0; index < 6; index++)
            await harness.AddAsync($"parallel-{index}", Now.AddSeconds(-10 - index));

        var running = harness.Worker.RunBatchAsync("scheduled.parallel");
        await agent.TargetEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ScheduledAiTaskWorker.MaximumConcurrency, agent.Calls);
        Assert.Equal(ScheduledAiTaskWorker.MaximumConcurrency, agent.PeakConcurrency);
        agent.Release.TrySetResult();
        var result = await running;

        Assert.Equal(6, result.Claimed);
        Assert.Equal(6, result.Completed);
        Assert.Equal(ScheduledAiTaskWorker.MaximumConcurrency, agent.PeakConcurrency);
        Assert.All(agent.Requests, request => Assert.Equal(AiRequestKind.ScheduledTask, request));
        Assert.False(agent.ReceivedApprovalGate);
        await using var verify = harness.CreateContext();
        Assert.Equal(6, await verify.ScheduledAiTaskWork.CountAsync(value => value.State == "completed"));
        Assert.Equal(6, await verify.Notifications.CountAsync(value => value.State == NotificationState.Read));
        Assert.Equal(6, await verify.Operations.CountAsync(value => value.Tool == "local-ai.scheduled-task"));
    }

    [Fact]
    public async Task Two_workers_cannot_deliver_the_same_unexpired_notification()
    {
        var time = new ManualTimeProvider(Now);
        var agent = new BlockingAgent(1);
        await using var harness = await Harness.CreateAsync(time, agent);
        await harness.AddAsync("duplicate", Now.AddMinutes(-1));
        var other = new ScheduledAiTaskWorker(harness.Scopes,
            NullLogger<ScheduledAiTaskWorker>.Instance, time);

        var first = harness.Worker.RunBatchAsync("scheduled.first");
        await agent.TargetEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = await other.RunBatchAsync("scheduled.second");

        Assert.Equal(0, duplicate.Claimed);
        Assert.Equal(1, agent.Calls);
        agent.Release.TrySetResult();
        Assert.Equal(1, (await first).Completed);
        await using var verify = harness.CreateContext();
        Assert.Single(await verify.Operations
            .Where(value => value.Tool == "local-ai.scheduled-task").ToArrayAsync());
        Assert.Equal(1, (await verify.ScheduledAiTaskWork.SingleAsync()).AttemptCount);
    }

    [Fact]
    public async Task Cancellation_leaves_a_lease_that_restart_recovers_after_expiry()
    {
        var time = new ManualTimeProvider(Now);
        var agent = new InterruptOnceAgent();
        await using var harness = await Harness.CreateAsync(time, agent);
        await harness.AddAsync("interrupted", Now.AddMinutes(-2));
        using var cancellation = new CancellationTokenSource();

        var interrupted = harness.Worker.RunBatchAsync("scheduled.interrupted", cancellation.Token);
        await agent.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => interrupted);

        await using (var stranded = harness.CreateContext())
        {
            var work = await stranded.ScheduledAiTaskWork.SingleAsync();
            Assert.Equal("leased", work.State);
            Assert.Equal(1, work.AttemptCount);
            Assert.Equal(NotificationState.Unread, (await stranded.Notifications.SingleAsync()).State);
            Assert.Empty(await stranded.Operations
                .Where(value => value.Tool == "local-ai.scheduled-task").ToArrayAsync());
        }

        time.Advance(SqliteScheduledAiTaskWorkStore.LeaseDuration.Add(TimeSpan.FromSeconds(1)));
        var recovered = await harness.Worker.RunBatchAsync("scheduled.restarted");

        Assert.Equal(1, recovered.Recovered);
        Assert.Equal(1, recovered.Completed);
        Assert.Equal(2, agent.Calls);
        await using var verify = harness.CreateContext();
        var final = await verify.ScheduledAiTaskWork.SingleAsync();
        Assert.Equal("completed", final.State);
        Assert.Equal(2, final.AttemptCount);
        Assert.Equal(NotificationState.Read, (await verify.Notifications.SingleAsync()).State);
    }

    [Fact]
    public async Task Queue_age_provider_duration_and_retry_failure_are_durable_and_audited()
    {
        var time = new ManualTimeProvider(Now);
        var agent = new TimedSequenceAgent(time,
            (TimeSpan.FromMilliseconds(250), AiResponse.Failure("PROVIDER_BUSY", "Try later.")),
            (TimeSpan.FromMilliseconds(400), Success("finished")));
        await using var harness = await Harness.CreateAsync(time, agent);
        await harness.AddAsync("metrics", Now.AddSeconds(-30));

        var first = await harness.Worker.RunBatchAsync("scheduled.metrics");

        Assert.Equal(1, first.Retried);
        await using (var retry = harness.CreateContext())
        {
            var work = await retry.ScheduledAiTaskWork.SingleAsync();
            Assert.Equal("retry", work.State);
            Assert.Equal(30_000, work.QueueAgeMilliseconds);
            Assert.Equal(250, work.ProviderDurationMilliseconds);
            Assert.Equal("PROVIDER_BUSY", work.FailureKind);
            var operation = await retry.Operations.SingleAsync(value =>
                value.Tool == "local-ai.scheduled-task");
            Assert.False(operation.Success);
            Assert.Equal("PROVIDER_BUSY", operation.Error);
            Assert.Contains("queue age 30000 ms", operation.Summary, StringComparison.Ordinal);
            Assert.Contains("provider duration 250 ms", operation.Summary, StringComparison.Ordinal);
        }

        time.Advance(SqliteScheduledAiTaskWorkStore.FirstRetryDelay);
        var second = await harness.Worker.RunBatchAsync("scheduled.metrics");

        Assert.Equal(1, second.Completed);
        await using var verify = harness.CreateContext();
        var completed = await verify.ScheduledAiTaskWork.SingleAsync();
        Assert.Equal("completed", completed.State);
        Assert.Equal(2, completed.AttemptCount);
        Assert.Equal(35_250, completed.QueueAgeMilliseconds);
        Assert.Equal(400, completed.ProviderDurationMilliseconds);
        Assert.Equal(2, await verify.Operations.CountAsync(value =>
            value.Tool == "local-ai.scheduled-task"));
    }

    [Fact]
    public async Task Expired_final_lease_becomes_visible_terminal_failure_without_redelivery()
    {
        var time = new ManualTimeProvider(Now);
        var agent = new TimedSequenceAgent(time);
        await using var harness = await Harness.CreateAsync(time, agent);
        var notificationId = await harness.AddAsync("exhausted", Now.AddMinutes(-20));
        await using (var setup = harness.CreateContext())
        {
            setup.ScheduledAiTaskWork.Add(new ScheduledAiTaskWorkRecord
            {
                NotificationId = notificationId,
                State = "leased",
                AttemptCount = 3,
                LeaseOwner = "scheduled.dead",
                LeaseToken = new string('a', 32),
                LeaseExpiresAtUtc = Now.AddSeconds(-1).UtcDateTime,
                QueueAgeMilliseconds = 1_000,
                Revision = 3,
                EnqueuedAtUtc = Now.AddMinutes(-20).UtcDateTime,
                UpdatedAtUtc = Now.AddSeconds(-1).UtcDateTime
            });
            await setup.SaveChangesAsync();
        }

        var result = await harness.Worker.RunBatchAsync("scheduled.recovery");

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Claimed);
        Assert.Equal(0, agent.Calls);
        await using var verify = harness.CreateContext();
        var work = await verify.ScheduledAiTaskWork.SingleAsync();
        Assert.Equal("failed", work.State);
        Assert.Equal("attempts-exhausted", work.FailureKind);
        Assert.Equal(NotificationState.Read, (await verify.Notifications.SingleAsync()).State);
        var operation = await verify.Operations.SingleAsync(value =>
            value.Tool == "local-ai.scheduled-task");
        Assert.False(operation.Success);
        Assert.Equal("SCHEDULED_AI_TASK_ATTEMPTS_EXHAUSTED", operation.Error);
    }

    [Fact]
    public async Task Migrated_queue_allows_owned_renewal_and_rejects_forged_transitions_and_deletion()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new DantesRoleplayDbContext(options);
        await db.Database.MigrateAsync();
        var id = new string('b', 32);
        db.Notifications.Add(new Notification
        {
            Id = id,
            Topic = ScheduledAiTaskProtocol.Topic,
            Subject = "guard",
            CorrelationId = id,
            CreatedAt = Now.UtcDateTime
        });
        await db.SaveChangesAsync();
        db.ScheduledAiTaskWork.Add(new ScheduledAiTaskWorkRecord
        {
            NotificationId = id,
            State = "ready",
            EnqueuedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        });
        await db.SaveChangesAsync();

        var transition = await Assert.ThrowsAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE scheduled_ai_task_work
                SET State = 'leased', AttemptCount = 2, LeaseOwner = 'forged.worker',
                    LeaseToken = {new string('c', 32)},
                    LeaseExpiresAtUtc = {Now.AddMinutes(1).UtcDateTime},
                    QueueAgeMilliseconds = 0, Revision = 1, UpdatedAtUtc = {Now.UtcDateTime}
                WHERE NotificationId = {id}
                """));
        var deletion = await Assert.ThrowsAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM scheduled_ai_task_work WHERE NotificationId = {id}
                """));

        Assert.Contains("SCHEDULED_AI_TASK_WORK_TRANSITION", transition.Message,
            StringComparison.Ordinal);
        var time = new ManualTimeProvider(Now);
        var store = new SqliteScheduledAiTaskWorkStore(db, time);
        var lease = Assert.Single((await store.ClaimBatchAsync("scheduled.owner")).Leases);
        time.Advance(SqliteScheduledAiTaskWorkStore.LeaseRenewalInterval);
        Assert.True(await store.RenewAsync(lease));

        var takeover = await Assert.ThrowsAsync<SqliteException>(() =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE scheduled_ai_task_work
                SET LeaseOwner = 'forged.worker', LeaseToken = {new string('d', 32)},
                    LeaseExpiresAtUtc = {time.GetUtcNow().AddMinutes(11).UtcDateTime},
                    Revision = Revision + 1, UpdatedAtUtc = {time.GetUtcNow().UtcDateTime}
                WHERE NotificationId = {id}
                """));
        Assert.Contains("SCHEDULED_AI_TASK_WORK_DELETE", deletion.Message,
            StringComparison.Ordinal);
        Assert.Contains("SCHEDULED_AI_TASK_WORK_TRANSITION", takeover.Message,
            StringComparison.Ordinal);
        var final = await db.ScheduledAiTaskWork.AsNoTracking().SingleAsync();
        Assert.Equal("leased", final.State);
        Assert.Equal("scheduled.owner", final.LeaseOwner);
    }

    private static AiResponse Success(string text) =>
        new(true, null, text, null, [], 0, 0);

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string path;
        private readonly ServiceProvider provider;
        private readonly DbContextOptions<DantesRoleplayDbContext> options;

        private Harness(
            string path,
            ServiceProvider provider,
            DbContextOptions<DantesRoleplayDbContext> options,
            TimeProvider time)
        {
            this.path = path;
            this.provider = provider;
            this.options = options;
            Scopes = provider.GetRequiredService<IServiceScopeFactory>();
            Worker = new ScheduledAiTaskWorker(Scopes, NullLogger<ScheduledAiTaskWorker>.Instance, time);
        }

        internal IServiceScopeFactory Scopes { get; }
        internal ScheduledAiTaskWorker Worker { get; }

        internal static async Task<Harness> CreateAsync(
            TimeProvider time,
            ISystemAiAgentService agent)
        {
            var path = Path.Combine(Path.GetTempPath(), $"scheduled-ai-{Guid.NewGuid():N}.db");
            var connectionString = $"Data Source={path};Pooling=False;Default Timeout=5";
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite(connectionString).Options;
            var services = new ServiceCollection();
            services.AddSingleton(time);
            services.AddSingleton(agent);
            services.AddSingleton<IApplicationActivationReader>(new StaticActivation());
            services.AddDbContext<DantesRoleplayDbContext>(value => value.UseSqlite(connectionString));
            services.AddScoped<INotificationStore, NotificationStore>();
            services.AddScoped<IOperationLog, OperationLog>();
            services.AddScoped<SqliteScheduledAiTaskWorkStore>();
            services.AddScoped<ScheduledAiTaskExecutor>();
            var provider = services.BuildServiceProvider();
            var harness = new Harness(path, provider, options, time);
            await using var db = harness.CreateContext();
            await db.Database.EnsureCreatedAsync();
            return harness;
        }

        internal DantesRoleplayDbContext CreateContext() => new(options);

        internal async Task<string> AddAsync(string suffix, DateTimeOffset createdAt)
        {
            var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(suffix))).ToLowerInvariant()[..32];
            var hash = new string('A', 64);
            var body = JsonSerializer.Serialize(new ScheduledAiTaskEnvelope(
                "example", hash, new("agent", "Agent", "Read and prepare only."),
                "test", "model", $"Run {suffix}.", AiReasoningEffort.Medium,
                "principal." + new string('a', 64), "test",
                PrivateOperatorAuthorizationPolicy.PrivateHostScope));
            await using var db = CreateContext();
            db.Notifications.Add(new Notification
            {
                Id = id,
                Topic = ScheduledAiTaskProtocol.Topic,
                Subject = suffix,
                Body = body,
                CorrelationId = id,
                CreatedAt = createdAt.UtcDateTime
            });
            await db.SaveChangesAsync();
            return id;
        }

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    private sealed class StaticActivation : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId)
        {
            var hash = new string('A', 64);
            return new(applicationId, 1, 1, hash, hash, hash, hash, hash, hash,
                "coverage-v1", true, [], [], "operation", Now.UtcDateTime)
            {
                ResolutionFingerprint = hash
            };
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly object gate = new();
        private DateTimeOffset now = now;
        private long timestamp;

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate) return now;
        }

        public override long GetTimestamp()
        {
            lock (gate) return timestamp;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        internal void Advance(TimeSpan duration)
        {
            lock (gate)
            {
                now = now.Add(duration);
                timestamp += duration.Ticks;
            }
        }
    }

    private sealed class BlockingAgent(int target) : ISystemAiAgentService
    {
        private int calls;
        private int running;
        private int peak;
        internal TaskCompletionSource TargetEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls => Volatile.Read(ref calls);
        internal int PeakConcurrency => Volatile.Read(ref peak);
        internal ConcurrentBag<AiRequestKind> Requests { get; } = [];
        internal bool ReceivedApprovalGate { get; private set; }

        public async Task<AiResponse> SendAsync(
            AiAgentProfile profile,
            AiRequest request,
            SystemCapabilityInvocationContext context,
            ISystemCapabilityAiWriteApprovalGate? writeApprovalGate = null,
            IAiToolApprovalGate? toolApprovalGate = null,
            CancellationToken cancellationToken = default)
        {
            if (writeApprovalGate is not null || toolApprovalGate is not null)
                ReceivedApprovalGate = true;
            Requests.Add(request.Kind);
            var count = Interlocked.Increment(ref calls);
            var current = Interlocked.Increment(ref running);
            UpdatePeak(current);
            if (count >= target) TargetEntered.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return Success("done");
            }
            finally { Interlocked.Decrement(ref running); }
        }

        private void UpdatePeak(int value)
        {
            while (true)
            {
                var observed = Volatile.Read(ref peak);
                if (value <= observed || Interlocked.CompareExchange(ref peak, value, observed) == observed)
                    return;
            }
        }
    }

    private sealed class InterruptOnceAgent : ISystemAiAgentService
    {
        private int calls;
        internal int Calls => Volatile.Read(ref calls);
        internal TaskCompletionSource FirstEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AiResponse> SendAsync(
            AiAgentProfile profile,
            AiRequest request,
            SystemCapabilityInvocationContext context,
            ISystemCapabilityAiWriteApprovalGate? writeApprovalGate = null,
            IAiToolApprovalGate? toolApprovalGate = null,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                FirstEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return Success("recovered");
        }
    }

    private sealed class TimedSequenceAgent(
        ManualTimeProvider time,
        params (TimeSpan Duration, AiResponse Response)[] responses) : ISystemAiAgentService
    {
        private readonly ConcurrentQueue<(TimeSpan Duration, AiResponse Response)> responses = new(responses);
        private int calls;
        internal int Calls => Volatile.Read(ref calls);

        public Task<AiResponse> SendAsync(
            AiAgentProfile profile,
            AiRequest request,
            SystemCapabilityInvocationContext context,
            ISystemCapabilityAiWriteApprovalGate? writeApprovalGate = null,
            IAiToolApprovalGate? toolApprovalGate = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            if (!responses.TryDequeue(out var response)) response = (TimeSpan.Zero, Success("done"));
            time.Advance(response.Duration);
            return Task.FromResult(response.Response);
        }
    }
}
