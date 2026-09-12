using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.MCPServer;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Registered_opt_in_validation_worker_drains_one_review_and_disabled_worker_does_not_dispatch()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"validation-worker-{Guid.NewGuid():N}.db");
        try
        {
            ApplicationCandidateReference candidate;
            ApplicationCandidateReuseInputV2 reviewInput;
            InteractionInvocationHost reviewHost;
            await using (var db = new DantesRoleplayDbContext(new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite("Filename=" + databasePath).Options))
            {
                await db.Database.EnsureCreatedAsync();
                var data = await PureRuntimeFixtureAsync(db,
                    "return { data: { count: ctx.input.count + 1 } };");
                candidate = data.Candidate;
                var gate = PureValidationGate(db, data.Setup);
                await using (var boundary = await SystemTaskValidationTransaction.OpenAsync(
                    db, TimeProvider.System, false, default))
                {
                    var prepared = await gate.CheckAsync(PureReviewHost(data.Setup, "worker-input-probe"),
                        candidate, true);
                    Assert.Null(SystemTaskApplicationValidationGate.ExecutionPrerequisite(prepared));
                    reviewInput = Assert.IsType<ApplicationCandidateReuseInputV2>(prepared.ReviewInput);
                }
                reviewHost = PureReviewHost(data.Setup, "production-worker-review");
            }

            var provider = new ProductionValidationProvider(reviewInput);
            SystemTaskDurableHandle handle;
            await using (var disabled = WorkerServices(databasePath, enabled: false, provider))
            {
                await using var scope = disabled.CreateAsyncScope();
                var submitted = await scope.ServiceProvider
                    .GetRequiredService<SystemTaskApplicationValidationService>()
                    .SubmitAsync(ValidationProfile(reviewHost, candidate));
                Assert.True(submitted.Tag == InteractionInvocationResultTag.Pending,
                    $"{submitted.Code}: {submitted.SafeMessage}");
                handle = Assert.IsType<SystemTaskDurableHandle>(submitted.TaskHandle);

                var worker = disabled.GetRequiredService<SystemTaskApplicationValidationBackgroundWorker>();
                Assert.False(await worker.RunOnceAsync("disabled-validation-worker"));
                Assert.Equal(0, provider.Calls);
                var queued = await new SqliteSystemTaskLifecycleStore(
                    "Filename=" + databasePath, TimeProvider.System).ReadAsync(handle);
                Assert.Equal(SystemTaskLifecycleState.Queued, queued!.State);
            }

            await using (var enabled = WorkerServices(databasePath, enabled: true, provider))
            {
                var worker = enabled.GetRequiredService<SystemTaskApplicationValidationBackgroundWorker>();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await worker.StartAsync(timeout.Token);
                var store = new SqliteSystemTaskLifecycleStore("Filename=" + databasePath, TimeProvider.System);
                SystemTaskLifecycleSnapshot? completed;
                do
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
                    completed = await store.ReadAsync(handle, timeout.Token);
                } while (completed?.State is SystemTaskLifecycleState.Queued or SystemTaskLifecycleState.Running);
                await worker.StopAsync(timeout.Token);

                Assert.Equal(SystemTaskLifecycleState.Completed, completed!.State);
                Assert.StartsWith("validation.result.", completed.CompletionEvidenceReference,
                    StringComparison.Ordinal);
                Assert.Contains("application-candidate-reuse-review-result/v1", completed.ResultJson,
                    StringComparison.Ordinal);
                Assert.Equal(1, provider.Calls);
                Assert.Equal("gpt-5.6-sol", provider.Request!.Model);
                Assert.Equal(AiReasoningEffort.Low, provider.Request.Reasoning);
                Assert.Empty(provider.Request.Tools);
                Assert.Null(provider.Request.ToolExecutor);
                Assert.Equal(0, provider.Request.MaximumToolCalls);
                Assert.Equal(2_048, provider.Request.MaximumOutputTokens);
                Assert.Equal(ApplicationCandidateReuseJudgmentLimits.OutputUtf8Bytes,
                    provider.Request.MaximumResponseBytes);
                Assert.InRange(provider.Request.MaximumDuration!.Value,
                    TimeSpan.FromTicks(1), TimeSpan.FromMinutes(10));

                await using var scope = enabled.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>();
                await using var boundary = await SystemTaskValidationTransaction.OpenAsync(
                    db, TimeProvider.System, false, timeout.Token);
                Assert.NotNull(await boundary.Store.ReadCompletedValidationProofAsync(
                    handle, boundary.Connection, boundary.Transaction, timeout.Token));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    private ServiceProvider WorkerServices(string databasePath, bool enabled,
        ProductionValidationProvider provider)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SystemTaskApplicationValidationWorkerOptions.SectionName}:Enabled"] = enabled.ToString(),
            [$"{SystemTaskApplicationValidationWorkerOptions.SectionName}:Model"] = "gpt-5.6-sol"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDantesRoleplayMcpServer("Filename=" + databasePath,
            allowedSourceRoots: new Dictionary<string, string> { ["fixture-root"] = root },
            publishedApplicationCatalogs: [Application.Value],
            hostConfiguration: configuration,
            blobStorageRoot: Path.Combine(root, "validation-worker-blobs"));
        services.AddSingleton<IAiService>(new AiService([provider]));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class ProductionValidationProvider(ApplicationCandidateReuseInputV2 input) : IAiProvider
    {
        internal int Calls { get; private set; }
        internal AiProviderRequest? Request { get; private set; }
        public AiProviderInfo Info { get; } = new("codex", "Controlled production-worker fixture");

        public Task<IReadOnlyList<AiModel>> ListModelsAsync(
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AiModel>>([]);

        public Task<AiProviderResponse> SendAsync(AiProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Request = request;
            var json = JsonSerializer.Serialize(new
            {
                format = ApplicationCandidateReuseJudgmentOutputV2.OutputDomain,
                input.SelectionFingerprint,
                input.InputFingerprint,
                input.ManualResultFingerprint,
                judgment = "uncertain",
                reason = "The retained evidence supports the bounded fixture judgment.",
                assessments = input.Alternatives.Select(target => new
                {
                    target,
                    judgment = "uncertain",
                    reason = "The exact authorized alternative was assessed."
                })
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Task.FromResult(new AiProviderResponse(true, null, "review", json, [],
                Usage: new(7, 2, 9, true)));
        }
    }
}
