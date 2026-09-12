using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.AI;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using DantesRoleplay.SystemCapabilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;

namespace DantesRoleplay.Authorization.Tests;

// Reuse the actual SQLite authoring/activation/standing-grant fixture. Runtime evidence is never
// supplied by a substitute closure reader, preparation adapter or authorization policy.
public sealed partial class SqliteStandingGrantTargetResolverTests
{
    private const string PureMarkdownPath = "content/mechanics/pure.md";
    private const string PureJavaScriptPath = "content/mechanics/pure.js";

    [Fact]
    public void Production_registration_resolves_authoring_with_the_actual_runtime_preparation_chain()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDantesRoleplayDataAccess("Filename=:memory:");
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<SqliteApplicationAuthoringService>(
            scope.ServiceProvider.GetRequiredService<IApplicationAuthoringService>());
        Assert.IsType<ApplicationCandidateRuntimeValidator>(
            scope.ServiceProvider.GetRequiredService<IApplicationCandidatePreparation>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SystemTaskApplicationValidationGate>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SystemTaskApplicationValidationService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SystemTaskAiInvocationLifecycleFactory>());
    }

    [Fact]
    public async Task Pure_candidate_runs_once_through_actual_durable_ai_lifecycle_and_rechecks_revocation()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"validation-ai-{Guid.NewGuid():N}.db");
        DantesRoleplayDbContext? ownedDb = null;
        ServiceProvider? ownedProvider = null;
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite("Filename=" + databasePath).Options;
            var db = ownedDb = new DantesRoleplayDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var data = await PureRuntimeFixtureAsync(db,
                "return { data: { count: ctx.input.count + 1 } };");
            var gate = PureValidationGate(db, data.Setup);
            var service = new SystemTaskApplicationValidationService(db, gate, TimeProvider.System);
            var reviewHost = PureReviewHost(data.Setup, "pure-ai-review");
            SystemTaskValidationAuthority prepared;
            await using (var boundary = await SystemTaskValidationTransaction.OpenAsync(db, TimeProvider.System, false, default))
                prepared = await gate.CheckAsync(PureReviewHost(data.Setup, "input-probe"), data.Candidate, true);
            Assert.Null(SystemTaskApplicationValidationGate.ExecutionPrerequisite(prepared));
            var provider = new RetainedReviewProvider(prepared.ReviewInput!);
            var invoker = new SystemInnerWorkerValidationInvoker(new AiService([provider]), TimeProvider.System);
            var services = new ServiceCollection();
            services.AddSingleton(db);
            services.AddSingleton(gate);
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            var serviceProvider = ownedProvider = services.BuildServiceProvider();
            var lifecycles = new SystemTaskAiInvocationLifecycleFactory(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);

            var submitted = await service.SubmitAsync(ValidationProfile(reviewHost, data.Candidate));
            Assert.Equal(InteractionInvocationResultTag.Pending, submitted.Tag);
            var handle = submitted.TaskHandle!;
            var store = new SqliteSystemTaskLifecycleStore("Filename=" + databasePath, TimeProvider.System);
            Assert.True(await service.RunNextAsync("validation-worker", invoker,
                ReviewerConfiguration(), lifecycles));

            var completed = await store.ReadAsync(handle);
            Assert.True(completed!.State == SystemTaskLifecycleState.Completed,
                completed.ErrorCode + ": " + completed.SafeMessage);
            Assert.StartsWith("validation.result.", completed.CompletionEvidenceReference, StringComparison.Ordinal);
            Assert.Contains("application-candidate-reuse-review-result/v1", completed.ResultJson, StringComparison.Ordinal);
            Assert.Equal(1, provider.Calls);
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                if (command.Connection!.State != System.Data.ConnectionState.Open) await command.Connection.OpenAsync();
                command.CommandText = "SELECT COUNT(*) FROM system_task_ai_dispatch_evidence WHERE kind='dispatch'";
                Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
                command.CommandText = "SELECT COUNT(*) FROM system_task_ai_dispatch_evidence WHERE kind='usage' AND is_complete=1";
                Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
            }

            var replay = await service.SubmitAsync(ValidationProfile(
                PureReviewHost(data.Setup, "pure-ai-review", reviewHost.Budget.DeadlineUtc), data.Candidate));
            Assert.Equal(handle, replay.TaskHandle);
            Assert.False(await service.RunNextAsync("validation-worker", invoker,
                ReviewerConfiguration(), lifecycles));
            Assert.Equal(1, provider.Calls);

            var revokedSubmission = await service.SubmitAsync(ValidationProfile(
                PureReviewHost(data.Setup, "pure-ai-review-revoked"), data.Candidate));
            Assert.Equal(InteractionInvocationResultTag.Pending, revokedSubmission.Tag);
            await RevokeGrantAsync(db);
            Assert.True(await service.RunNextAsync("validation-worker", invoker,
                ReviewerConfiguration(), lifecycles));
            var denied = await store.ReadAsync(revokedSubmission.TaskHandle!);
            Assert.Equal(SystemTaskLifecycleState.Failed, denied!.State);
            Assert.Equal("INNER_VALIDATION_NOT_AUTHORIZED", denied.ErrorCode);
            Assert.Equal(1, provider.Calls);
        }
        finally
        {
            ownedProvider?.Dispose();
            if (ownedDb is not null) await ownedDb.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reviewed_pure_mechanic_validates_publishes_and_executes_from_retained_durable_proof(
        bool addDefinition)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"reviewed-pure-publication-{Guid.NewGuid():N}.db");
        DantesRoleplayDbContext? ownedDb = null;
        ServiceProvider? ownedProvider = null;
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite("Filename=" + databasePath).Options;
            var db = ownedDb = new DantesRoleplayDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var data = await PureRuntimeFixtureAsync(db,
                "return { data: { count: ctx.input.count + 1 } };");
            await AllowPurePublicationAsync(db, execute: true);
            db.ChangeTracker.Clear();
            var original = data.Setup.Activation.Current(Application)!;
            var markdownPath = addDefinition ? "content/mechanics/added.md" : PureMarkdownPath;
            var javascriptPath = addDefinition ? "content/mechanics/added.js" : PureJavaScriptPath;
            var definitionId = addDefinition ? "demo.runtime.pure.added" : "demo.runtime.pure.sample";
            var markdown = """
                ---
                id: demo.runtime.pure.added
                category: runtime.pure
                name: Added pure mechanic
                status: active
                ---

                ## Description
                Calculate a distinct state-free result.

                ## Requirements
                ```json
                {"inputSchema":{"type":"object","required":["count"],"properties":{"count":{"type":"integer"}}}}
                ```
                """.Replace("demo.runtime.pure.added", definitionId, StringComparison.Ordinal);
            var written = await Service(db, data.Setup).WriteCandidateAsync(
                PureAtomicHost(data.Setup, "reviewed-new-write", 1),
                new(null, 0, original.ActivationFingerprint, "runtime", null,
                    "Add a separate pure calculation because the existing operation has a different contract.",
                    [new("file:" + markdownPath, "catalog", markdownPath, "text/markdown", markdown),
                     new("file:" + javascriptPath, "catalog", javascriptPath, "text/javascript",
                         "return { data: { count: ctx.input.count + 2 } };")]));
            Assert.Equal(InteractionInvocationResultTag.Committed, written.Tag);
            var candidateRow = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking()
                .SingleAsync(value => value.SourceOperationId == written.Receipt!.OperationId);
            var candidate = new ApplicationCandidateReference(Application, candidateRow.CandidateId,
                candidateRow.Revision, candidateRow.ContentFingerprint);

            var gate = PureValidationGate(db, data.Setup);
            SystemTaskValidationAuthority prepared;
            await using (var boundary = await SystemTaskValidationTransaction.OpenAsync(db, TimeProvider.System, false, default))
                prepared = await gate.CheckAsync(PureReviewHost(data.Setup, "reviewed-new-probe"), candidate, true);
            Assert.Null(SystemTaskApplicationValidationGate.ExecutionPrerequisite(prepared));
            var definition = Assert.Single(prepared.PureClosure!.Definitions).Plan.Definition;
            Assert.Equal(definitionId, definition.DefinitionId);
            var provider = new RetainedReviewProvider(prepared.ReviewInput!,
                addDefinition ? "justifiedNew" : "extendExisting");
            var invoker = new SystemInnerWorkerValidationInvoker(new AiService([provider]), TimeProvider.System);
            var validationTasks = new SystemTaskApplicationValidationService(db, gate, TimeProvider.System);
            var services = new ServiceCollection();
            services.AddSingleton(db);
            services.AddSingleton(gate);
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            var serviceProvider = ownedProvider = services.BuildServiceProvider();
            var lifecycles = new SystemTaskAiInvocationLifecycleFactory(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
            var reviewHost = PureReviewHost(data.Setup, "reviewed-new-task");
            var submitted = await validationTasks.SubmitAsync(ValidationProfile(reviewHost, candidate),
                written.Receipt!.OperationId, "reviewed-new-write");
            Assert.Equal(InteractionInvocationResultTag.Pending, submitted.Tag);
            var handle = Assert.IsType<SystemTaskDurableHandle>(submitted.TaskHandle);
            Assert.True(await validationTasks.RunNextAsync("reviewed-new-worker", invoker,
                ReviewerConfiguration(), lifecycles));
            var taskStore = new SqliteSystemTaskLifecycleStore("Filename=" + databasePath, TimeProvider.System);
            var completed = await taskStore.ReadAsync(handle);
            Assert.Equal(SystemTaskLifecycleState.Completed, completed!.State);
            Assert.Equal(1, provider.Calls);
            await using (var proofBoundary = await SystemTaskValidationTransaction.OpenAsync(db, TimeProvider.System, false, default))
                Assert.NotNull(await proofBoundary.Store.ReadCompletedValidationProofAsync(handle,
                    proofBoundary.Connection, proofBoundary.Transaction));

            var reviewed = new ApplicationCandidateReviewedPureUpdateReader(db, data.Setup.Applications,
                data.Setup.Activation, data.Setup.Activation, data.Setup.Resolver, gate);
            await using (var reviewBoundary = await SystemTaskValidationTransaction.OpenAsync(db, TimeProvider.System, false, default))
                Assert.NotNull(await reviewed.ReadAsync(PureAtomicHost(data.Setup, "reviewed-new-read", 3), candidate));
            var usage = await db.Set<SystemTaskAiDispatchEvidenceRecord>()
                .SingleAsync(value => value.Kind == "usage");
            usage.ObservedToolCalls = 1;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await using (var corruptBoundary = await SystemTaskValidationTransaction.OpenAsync(db, TimeProvider.System, false, default))
                Assert.Null(await reviewed.ReadAsync(PureAtomicHost(data.Setup, "reviewed-new-accounting", 3), candidate));
            usage = await db.Set<SystemTaskAiDispatchEvidenceRecord>().SingleAsync(value => value.Kind == "usage");
            usage.ObservedToolCalls = 0;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var authoring = new SqliteApplicationAuthoringService(db, data.Setup.Applications,
                data.Setup.Activation, data.Setup.Activation, data.Setup.Sources,
                new SqliteStandingGrantPolicy(db, data.Setup.Resolver), data.Setup.Resolver,
                new OperationLog(db), PureRuntimeValidator(db, data.Setup), null, reviewed);

            await RevokeGrantAsync(db);
            db.ChangeTracker.Clear();
            var revoked = await authoring.ValidateAsync(new ApplicationCandidateValidationRequest(candidate,
                [new(definition, "{\"count\":1}", "{\"count\":3}")]),
                PureAtomicHost(data.Setup, "reviewed-new-revoked", 3));
            Assert.NotEqual(InteractionInvocationResultTag.Committed, revoked.Tag);
            var currentGrant = await db.Set<StandingGrantCurrentRecord>().SingleAsync(value => value.GrantId == "grant");
            currentGrant.Revision = 1;
            db.Remove(await db.Set<StandingGrantRevisionRecord>().SingleAsync(
                value => value.GrantId == "grant" && value.Revision == 2));
            db.Remove(await db.Operations.SingleAsync(value => value.Id == "grant-revoke"));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var validated = await authoring.ValidateAsync(new ApplicationCandidateValidationRequest(candidate,
                [new(definition, "{\"count\":1}", "{\"count\":3}")]),
                PureAtomicHost(data.Setup, "reviewed-new-validate", 3));
            Assert.Equal(InteractionInvocationResultTag.Committed, validated.Tag);
            var validation = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
                .SingleAsync(value => value.CandidateId == candidate.CandidateId);
            Assert.True(validation.Outcome == "valid", validation.DiagnosticsJson);
            Assert.True(validation.DependenciesComplete);
            Assert.Equal(completed.CompletionEvidenceReference, validation.ReuseEvidenceReference);
            var replayedValidation = await authoring.ValidateAsync(new ApplicationCandidateValidationRequest(candidate,
                [new(definition, "{\"count\":1}", "{\"count\":3}")]),
                PureAtomicHost(data.Setup, "reviewed-new-validate", 3));
            Assert.Equal(validated.Receipt, replayedValidation.Receipt);
            Assert.Equal(1, provider.Calls);

            var catalogs = new ActivatedApplicationCatalogProvider(
                new ConfiguredPublicApplicationCatalogPolicy([Application.Value]),
                new ActivatedApplicationCatalogMaterializer(data.Setup.Applications, data.Setup.Activation,
                    data.Setup.Sources, data.Setup.Roots, data.Setup.Extensions)
                    .UsePreparationCache(new ActivatedApplicationCatalogSnapshotCache(),
                        new ActivatedApplicationCatalogCacheAuthority()),
                new CatalogCursorCodec(Enumerable.Repeat((byte)0x51, 32).ToArray()), data.Setup.Activation);
            Assert.True(catalogs.TryGetSnapshot(Application, out var before));
            var lifecycleRow = await db.Set<SystemTaskLifecycleRecord>()
                .SingleAsync(value => value.TaskId == handle.TaskId);
            var originalResult = lifecycleRow.ResultJson!;
            lifecycleRow.ResultJson = "{\"tampered\":true}";
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var blocked = await authoring.ActivateAsync(PureAtomicHost(data.Setup, "reviewed-new-tampered", 3),
                new(candidate, validation.OperationId));
            Assert.NotEqual(InteractionInvocationResultTag.Committed, blocked.Tag);
            Assert.Equal(original.ActivationFingerprint, data.Setup.Activation.Current(Application)!.ActivationFingerprint);
            lifecycleRow = await db.Set<SystemTaskLifecycleRecord>()
                .SingleAsync(value => value.TaskId == handle.TaskId);
            lifecycleRow.ResultJson = originalResult;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var activated = await authoring.ActivateAsync(PureAtomicHost(data.Setup, "reviewed-new-activate", 3),
                new(candidate, validation.OperationId));
            Assert.Equal(InteractionInvocationResultTag.Committed, activated.Tag);
            Assert.True(catalogs.TryGetSnapshot(Application, out var after));
            Assert.NotEqual(before.EffectiveSetFingerprint, after.EffectiveSetFingerprint);
            Assert.True(catalogs.TryGet(Application, out var catalog));
            var record = catalog.Inspect(new(Application, Application.Value, definitionId)).Summary;
            Assert.Equal(definition.ContentFingerprint, record.ContentFingerprint);
            var activeRevision = data.Setup.Activation.Current(Application)!.ActivationRevision;
            var replayed = await authoring.ActivateAsync(PureAtomicHost(data.Setup, "reviewed-new-activate", 3),
                new(candidate, validation.OperationId));
            Assert.Equal(activated.Receipt, replayed.Receipt);
            Assert.Equal(activeRevision, data.Setup.Activation.Current(Application)!.ActivationRevision);

            var schemas = new BoundedJsonSchemaValidator();
            var executor = new ApplicationPureActionExecutor(catalogs, new(schemas), new JintMechanicEngine(), schemas,
                data.Setup.Resolver, new SqliteStandingGrantPolicy(db, data.Setup.Resolver),
                new SqliteEcsWriteTransactionFactory(db));
            var executed = await executor.ExecuteAsync(new(PureAtomicHost(data.Setup, "reviewed-new-execute", 1),
                record.QualifiedId, record.Version, record.ContentFingerprint,
                new Dictionary<string, string>(), "{\"count\":3}"));
            Assert.Equal(InteractionInvocationResultTag.Completed, executed.Tag);
            Assert.Equal("{\"count\":5}", executed.DataJson);
        }
        finally
        {
            ownedProvider?.Dispose();
            if (ownedDb is not null) await ownedDb.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Authoring_retains_actual_jint_report_and_replays_only_the_verified_durable_proof()
    {
        await using var db = fixture.CreateContext();
        var data = await PureRuntimeFixtureAsync(db,
            "return { data: { count: ctx.input.count + 1 } };");
        var service = Service(db, data.Setup, PureRuntimeValidator(db, data.Setup));
        var request = new ApplicationCandidateValidationRequest(data.Candidate,
            [new(data.Definition, "{\"count\":1}", "{\"count\":2}")]);
        var host = PureRuntimeHost(data.Setup, operations: 2);

        var first = await service.ValidateAsync(request, host);

        Assert.Equal(InteractionInvocationResultTag.Committed, first.Tag);
        Assert.Equal(0, host.Budget.RemainingOperations);
        var row = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking().SingleAsync();
        Assert.Equal("unavailable", row.Outcome);
        Assert.False(row.DependenciesComplete);
        Assert.Equal(ApplicationCandidateRuntimeValidator.RuntimePolicyVersion + "@"
            + ApplicationCandidateRuntimeValidator.RuntimePolicyFingerprint, row.PreparationVersion);
        var operation = await db.Operations.AsNoTracking().SingleAsync(value => value.Id == row.OperationId);
        Assert.True(ApplicationCandidateOperationProof.TryReadRuntimeReport(operation, row,
            data.Candidate, data.Definitions, out var report));
        Assert.Equal(ApplicationCandidateRuntimeStatus.Completed, report!.Status);
        Assert.Equal(report.SelectionEvidenceFingerprint,
            (await PureRuntimeClosure(db, data.Setup).ReadAsync(PureRuntimeHost(data.Setup), data.Candidate))!.EvidenceFingerprint);
        Assert.Equal(row.OperationId + "#runtime-report."
            + ApplicationCandidateOperationProof.RuntimeReportFingerprint(report), row.PreparedEvidenceReference);

        var replayHost = PureRuntimeHost(data.Setup, operations: 2);
        var replay = await service.ValidateAsync(request, replayHost);
        Assert.Equal(first.Receipt, replay.Receipt);
        Assert.Equal(1, replayHost.Budget.RemainingOperations);

        var inspected = await service.InspectAsync(PureRuntimeHost(data.Setup),
            new(data.Candidate.CandidateId, data.Candidate.Revision));
        Assert.Equal(InteractionInvocationResultTag.Completed, inspected.Tag);
        using var inspection = JsonDocument.Parse(inspected.DataJson!);
        Assert.Equal((int)ApplicationCandidateRuntimeStatus.Completed,
            inspection.RootElement.GetProperty("validation").GetProperty("RuntimeReport").GetProperty("Status").GetInt32());

        var mutableOperation = await db.Operations.SingleAsync(value => value.Id == row.OperationId);
        var guard = JsonNode.Parse(mutableOperation.GuardEvidenceJson)!.AsObject();
        guard["runtimeReportFingerprint"] = new string('A', 64);
        mutableOperation.GuardEvidenceJson = InteractionCanonicalJson.CanonicalizeObject(guard.ToJsonString());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var inconsistent = await service.InspectAsync(PureRuntimeHost(data.Setup),
            new(data.Candidate.CandidateId, data.Candidate.Revision));
        Assert.Contains("APPLICATION_CANDIDATE_VALIDATION_EVIDENCE_INCONSISTENT", inconsistent.DataJson);
    }

    [Fact]
    public async Task Authoring_retains_runtime_mismatch_as_invalid_and_current_revocation_blocks_replay()
    {
        await using var db = fixture.CreateContext();
        var data = await PureRuntimeFixtureAsync(db,
            "return { data: { count: ctx.input.count } };");
        var service = Service(db, data.Setup, PureRuntimeValidator(db, data.Setup));
        var request = new ApplicationCandidateValidationRequest(data.Candidate,
            [new(data.Definition, "{\"count\":1}", "{\"count\":2}")]);

        var first = await service.ValidateAsync(request, PureRuntimeHost(data.Setup, operations: 2));

        Assert.Equal(InteractionInvocationResultTag.Committed, first.Tag);
        var row = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking().SingleAsync();
        var operation = await db.Operations.AsNoTracking().SingleAsync(value => value.Id == row.OperationId);
        Assert.Equal("invalid", row.Outcome);
        Assert.True(ApplicationCandidateOperationProof.TryReadRuntimeReport(operation, row,
            data.Candidate, data.Definitions, out var report));
        Assert.Equal(ApplicationCandidateRuntimeStatus.Invalid, report!.Status);
        Assert.Equal("PURE_RUNTIME_DATA_MISMATCH", Assert.Single(report.Diagnostics).Code);

        await RevokeGrantAsync(db);
        db.ChangeTracker.Clear();
        var replay = await service.ValidateAsync(request, PureRuntimeHost(data.Setup, operations: 2));
        Assert.NotEqual(InteractionInvocationResultTag.Committed, replay.Tag);
        Assert.Null(replay.Receipt);
    }

    [Fact]
    public async Task Pure_runtime_cancellation_after_engine_admission_keeps_the_attempt_and_consumed_allowance()
    {
        await using var db = fixture.CreateContext();
        var data = await PureRuntimeFixtureAsync(db, "return { data: {} };");
        await using var transaction = await db.Database.BeginTransactionAsync();
        using var cancellation = new CancellationTokenSource();
        var engine = new JintMechanicEngine(preparedProgramCacheEnabled: true,
            preparationStarted: _ => cancellation.Cancel());
        var host = PureRuntimeHost(data.Setup);
        var report = await PureRuntimeValidator(db, data.Setup, engine).CheckAsync(
            new(data.Candidate, [new(data.Definition, "{}", "{}")]), host, cancellation.Token);

        Assert.Equal(ApplicationCandidateRuntimeStatus.Unavailable, report.Status);
        var sample = Assert.Single(report.Samples);
        Assert.True(sample.Attempted);
        Assert.Equal(ApplicationCandidateRuntimeStatus.Unavailable, sample.Outcome);
        Assert.Null(sample.ActualDataFingerprint);
        Assert.Equal(0, host.Budget.RemainingOperations);
    }

    [Fact]
    public async Task Pure_runtime_preserves_request_ordinals_across_interleaved_definitions()
    {
        await using var db = fixture.CreateContext();
        var data = await PureRuntimeFixtureAsync(db, "return { data: {} };", twoDefinitions: true);
        await using var transaction = await db.Database.BeginTransactionAsync();
        Assert.Equal(2, data.Definitions.Count);
        var report = await PureRuntimeValidator(db, data.Setup).CheckAsync(new(data.Candidate,
            [new(data.Definitions[0], "{}", "{}"), new(data.Definitions[1], "{}", "{}"),
             new(data.Definitions[0], "{}", "{}")]), PureRuntimeHost(data.Setup, operations: 3));

        Assert.Equal(ApplicationCandidateRuntimeStatus.Completed, report.Status);
        Assert.Equal([0, 1, 2], report.Samples.Select(sample => sample.SampleIndex));
    }

    [Fact]
    public async Task Pure_runtime_executes_retained_javascript_only_change_with_real_authority_and_exact_evidence()
    {
        await using var db = fixture.CreateContext();
        var data = await PureRuntimeFixtureAsync(db, "return { data: { count: ctx.input.count + 1 } };");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var host = PureRuntimeHost(data.Setup, operations: 2);
        var request = new ApplicationCandidateValidationRequest(data.Candidate,
            [new(data.Definition, "{\"count\":1}", "{\"count\":2}"),
             new(data.Definition, "{\"count\":5}", "{\"count\":6}")]);
        var active = data.Setup.Activation.Current(Application)!.ActivationFingerprint;
        var operationsBefore = await db.Operations.CountAsync();

        var report = await PureRuntimeValidator(db, data.Setup).CheckAsync(request, host);

        Assert.Equal(ApplicationCandidateRuntimeStatus.Completed, report.Status);
        Assert.Equal(data.Candidate, report.Candidate);
        Assert.Equal(0, host.Budget.RemainingOperations);
        Assert.Equal([0, 1], report.Samples.Select(sample => sample.SampleIndex));
        Assert.All(report.Samples, sample =>
        {
            Assert.True(sample.Attempted);
            Assert.Equal(ApplicationCandidateRuntimeStatus.Completed, sample.Outcome);
            Assert.Equal(sample.ExpectedDataFingerprint, sample.ActualDataFingerprint);
            Assert.Equal(data.Definition, sample.Definition);
        });
        Assert.Equal(ApplicationCandidateRuntimeValidator.RuntimePolicyVersion, report.RuntimePolicyVersion);
        Assert.Equal(ApplicationCandidateRuntimeValidator.RuntimePolicyFingerprint, report.RuntimePolicyFingerprint);
        Assert.Equal(64, report.RuntimePolicyFingerprint!.Length);
        var proof = await PureRuntimeClosure(db, data.Setup).ReadAsync(PureRuntimeHost(data.Setup), data.Candidate);
        Assert.Equal(proof!.EvidenceFingerprint, report.SelectionEvidenceFingerprint);
        var broad = await new ApplicationCandidateSelectionReader(db, data.Setup.Applications,
            data.Setup.Activation, data.Setup.Resolver).ReadAsync(PureRuntimeHost(data.Setup), data.Candidate);
        Assert.False(broad!.DependenciesComplete);
        Assert.Equal(active, data.Setup.Activation.Current(Application)!.ActivationFingerprint);
        Assert.Equal(operationsBefore, await db.Operations.CountAsync());
        Assert.Empty(await db.Set<ApplicationCandidateValidationRecord>().ToArrayAsync());
        Assert.Empty(report.Diagnostics);
    }

    [Theory]
    [InlineData("return { data: { count: 2 }, narration: 'unexpected' };")]
    [InlineData("return { data: { count: 2 }, decision: 'allow' };")]
    [InlineData("return { narration: 'no data' };")]
    [InlineData("return { data: [2] };")]
    [InlineData("return { data: { oversized: 'x'.repeat(70000) } };")]
    [InlineData("return { data: { count: 2 }, effects: [{ type: 'invented' }] };")]
    [InlineData("throw new Error('private diagnostic that must not escape');")]
    [InlineData("return { data: ctx.query('unavailable', {}) };")]
    [InlineData("return { data: ;")]
    public async Task Pure_runtime_rejects_non_data_or_failed_programs_without_exposing_engine_details(string source)
    {
        await using var db = fixture.CreateContext();
        var data = await PureRuntimeFixtureAsync(db, source);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var host = PureRuntimeHost(data.Setup);
        var report = await PureRuntimeValidator(db, data.Setup).CheckAsync(
            new(data.Candidate, [new(data.Definition, "{}", "{\"count\":2}")]), host);

        Assert.Equal(ApplicationCandidateRuntimeStatus.Invalid, report.Status);
        Assert.True(Assert.Single(report.Samples).Attempted);
        Assert.Null(report.Samples[0].ActualDataFingerprint);
        Assert.Equal(0, host.Budget.RemainingOperations);
        Assert.DoesNotContain("private diagnostic", System.Text.Json.JsonSerializer.Serialize(report), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pure_runtime_distinguishes_schema_rejection_and_data_mismatch()
    {
        await using var db = fixture.CreateContext();
        var data = await PureRuntimeFixtureAsync(db, "return { data: { count: ctx.input.count } };",
            "{\"inputSchema\":{\"type\":\"object\",\"required\":[\"count\"],\"properties\":{\"count\":{\"type\":\"integer\"}}}}");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var invalidHost = PureRuntimeHost(data.Setup);
        var invalid = await PureRuntimeValidator(db, data.Setup).CheckAsync(
            new(data.Candidate, [new(data.Definition, "{\"count\":\"wrong\"}", "{}")]), invalidHost);
        Assert.Equal(ApplicationCandidateRuntimeStatus.Invalid, invalid.Status);
        Assert.False(Assert.Single(invalid.Samples).Attempted);
        Assert.Null(invalid.Samples[0].ActualDataFingerprint);
        Assert.Equal(1, invalidHost.Budget.RemainingOperations);

        var mismatch = await PureRuntimeValidator(db, data.Setup).CheckAsync(
            new(data.Candidate, [new(data.Definition, "{\"count\":1}", "{\"count\":2}")]), PureRuntimeHost(data.Setup));
        Assert.Equal(ApplicationCandidateRuntimeStatus.Invalid, mismatch.Status);
        var sample = Assert.Single(mismatch.Samples);
        Assert.True(sample.Attempted);
        Assert.Equal(ApplicationCandidateRuntimeValidator.DataFingerprint("{\"count\":1}"), sample.ActualDataFingerprint);
        Assert.NotEqual(sample.ExpectedDataFingerprint, sample.ActualDataFingerprint);
    }

    [Fact]
    public async Task Pure_runtime_missing_samples_and_shared_budget_exhaustion_stay_unavailable()
    {
        await using var db = fixture.CreateContext();
        var data = await PureRuntimeFixtureAsync(db, "return { data: {} };");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var host = PureRuntimeHost(data.Setup);
        var missing = await PureRuntimeValidator(db, data.Setup).CheckAsync(new(data.Candidate, []), host);
        Assert.Equal(ApplicationCandidateRuntimeStatus.Unavailable, missing.Status);
        Assert.Empty(missing.Samples);
        Assert.Equal(1, host.Budget.RemainingOperations);

        var limited = await PureRuntimeValidator(db, data.Setup).CheckAsync(new(data.Candidate,
            [new(data.Definition, "{}", "{}"), new(data.Definition, "{}", "{}")]), host);
        Assert.Equal(ApplicationCandidateRuntimeStatus.Unavailable, limited.Status);
        Assert.Equal(ApplicationCandidateRuntimeStatus.Completed, Assert.Single(limited.Samples).Outcome);
        Assert.Equal(0, host.Budget.RemainingOperations);
    }

    [Fact]
    public async Task Pure_runtime_requires_the_authoring_transaction_and_bounds_the_whole_sample_request()
    {
        await using var db = fixture.CreateContext();
        var data = await PureRuntimeFixtureAsync(db, "return { data: {} };");
        var host = PureRuntimeHost(data.Setup);
        var validator = PureRuntimeValidator(db, data.Setup);
        var noTransaction = await validator.CheckAsync(new(data.Candidate,
            [new(data.Definition, "{}", "{}")]), host);
        Assert.Equal(ApplicationCandidateRuntimeStatus.Unavailable, noTransaction.Status);
        Assert.Empty(noTransaction.Samples);
        Assert.Null(noTransaction.SelectionEvidenceFingerprint);
        Assert.Equal(1, host.Budget.RemainingOperations);

        await using var transaction = await db.Database.BeginTransactionAsync();
        var individuallyBounded = "{\"payload\":\"" + new string('a', 33_000) + "\"}";
        await Assert.ThrowsAsync<InteractionContractException>(() => validator.CheckAsync(
            new(data.Candidate, [new(data.Definition, individuallyBounded, individuallyBounded)]), host));
        Assert.Equal(1, host.Budget.RemainingOperations);
    }

    [Theory]
    [InlineData("closure")]
    [InlineData("revoked")]
    [InlineData("candidate")]
    [InlineData("deadline")]
    public async Task Pure_runtime_unavailable_evidence_or_authority_never_starts_a_program(string failure)
    {
        await using var db = fixture.CreateContext();
        var data = await PureRuntimeFixtureAsync(db, "return { data: {} };",
            failure == "closure" ? "{\"roles\":{}}" : "{}");
        if (failure == "revoked") await RevokeGrantAsync(db);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var host = PureRuntimeHost(data.Setup,
            deadline: failure == "deadline" ? DateTime.UtcNow.AddSeconds(-1) : null);
        var candidate = failure == "candidate" ? data.Candidate with { ContentFingerprint = new string('A', 64) } : data.Candidate;
        var report = await PureRuntimeValidator(db, data.Setup).CheckAsync(
            new(candidate, [new(data.Definition, "{}", "{}")]), host);

        Assert.NotEqual(ApplicationCandidateRuntimeStatus.Completed, report.Status);
        Assert.Empty(report.Samples);
        Assert.Equal(1, host.Budget.RemainingOperations);
    }

    private async Task<(SetupState Setup, ApplicationCandidateReference Candidate,
        StandingGrantDefinitionReference Definition, IReadOnlyList<StandingGrantDefinitionReference> Definitions)>
        PureRuntimeFixtureAsync(DantesRoleplayDbContext db, string source, string requirements = "{}", bool twoDefinitions = false)
    {
        var setup = Setup(db);
        setup.Namespaces.Register(new CatalogNamespaceRegistration("demo.runtime.pure", "human-domain-label", "Pure runtime fixtures.",
            [CatalogNamespaceKinds.Mechanic], ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed generic fixture."));
        var markdown = $$"""
            ---
            id: demo.runtime.pure.sample
            category: runtime.pure
            name: Pure sample
            status: active
            ---

            ## Description
            A generic pure sample fixture.

            ## Requirements
            ```json
            {{requirements}}
            ```
            """;
        var directory = Path.Combine(root, "content", "mechanics");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "pure.md"), markdown, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "pure.js"), "return { data: { initial: true } };", new UTF8Encoding(false));
        var changes = new List<ApplicationCandidateDocumentInput>
        {
            new("file:" + PureJavaScriptPath, "catalog", PureJavaScriptPath, "text/javascript", source)
        };
        var paths = new List<string> { PureMarkdownPath, PureJavaScriptPath };
        if (twoDefinitions)
        {
            File.WriteAllText(Path.Combine(directory, "second.md"), markdown.Replace("pure.sample", "pure.second", StringComparison.Ordinal), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "second.js"), "return { data: { initial: true } };", new UTF8Encoding(false));
            paths.Add("content/mechanics/second.md");
            paths.Add("content/mechanics/second.js");
            changes.Add(new("file:content/mechanics/second.js", "catalog", "content/mechanics/second.js", "text/javascript", source));
        }
        await ActivateAsync(setup);
        await SeedPureRuntimeGrantAsync(db);
        var written = await Service(db, setup).WriteCandidateAsync(PureRuntimeHost(setup),
            new(null, 0, setup.Activation.Current(Application)!.ActivationFingerprint, "runtime", null,
                "Exercise retained pure runtime checks.",
                changes));
        Assert.Equal(InteractionInvocationResultTag.Committed, written.Tag);
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().ToArrayAsync());
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
        var reader = new ApplicationCandidateRetainedReader(db, setup.Applications);
        var metadata = await reader.ReadMetadataAsync(Application, candidate.CandidateId, candidate.Revision);
        var documents = await reader.ReadSelectedAsync(metadata!, paths);
        var definitions = SqliteApplicationAuthoringService.Definitions(Application, documents, changes.Select(value => value.RelativePath).ToArray());
        Assert.Equal(twoDefinitions ? 2 : 1, definitions.Count);
        return (setup, candidate, definitions.Single(value => value.DefinitionId == "demo.runtime.pure.sample"), definitions);
    }

    private static InteractionInvocationHost PureRuntimeHost(SetupState setup, int operations = 1, DateTime? deadline = null) =>
        InteractionInvocationHost.ForApplication(
            TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
            new(Application, 1, setup.Applications.Get(Application)!.Fingerprint, []), "grant@1", "pure-runtime",
            InteractionExecutionProfile.Atomic, new(operations, deadline ?? DateTime.UtcNow.AddMinutes(1)));

    private static InteractionInvocationHost PureAtomicHost(SetupState setup, string command, int operations) =>
        InteractionInvocationHost.ForApplication(
            TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
            new(Application, 1, setup.Applications.Get(Application)!.Fingerprint, []), "grant@1", command,
            InteractionExecutionProfile.Atomic, new(operations, DateTime.UtcNow.AddMinutes(2)));

    private static InteractionInvocationHost PureReviewHost(SetupState setup, string command, DateTime? deadline = null) =>
        InteractionInvocationHost.ForApplication(
            TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
            new(Application, 1, setup.Applications.Get(Application)!.Fingerprint, []), "grant@1", command,
            InteractionExecutionProfile.ReadOnly, new(16, deadline ?? DateTime.UtcNow.AddMinutes(2)));

    private static SystemTaskApplicationValidationGate PureValidationGate(
        DantesRoleplayDbContext db, SetupState setup)
    {
        var materializer = new ActivatedApplicationCatalogMaterializer(setup.Applications, setup.Activation,
            setup.Sources, setup.Roots, setup.Extensions).UsePreparationCache(
                new ActivatedApplicationCatalogSnapshotCache(), new ActivatedApplicationCatalogCacheAuthority());
        var snapshots = new ActivatedApplicationCatalogProvider(new ConfiguredPublicApplicationCatalogPolicy([Application.Value]),
            materializer, new CatalogCursorCodec(RandomNumberGenerator.GetBytes(32)), setup.Activation);
        var features = new InteractionFeatureRetriever(snapshots, namespaces: setup.Namespaces, changes: setup.Activation);
        var policy = new SqliteStandingGrantPolicy(db, setup.Resolver);
        var manuals = new InteractionManualContextService(new ProcedureStore(db), features, policy,
            setup.Resolver, setup.Activation, ["system"]);
        return new(db, setup.Applications, setup.Activation, setup.Resolver, policy, TimeProvider.System,
            PureRuntimeClosure(db, setup), manuals, features);
    }

    private static AiRequest ReviewerConfiguration() => new("host-provider", "host-model", [],
        Reasoning: AiReasoningEffort.Medium, AllowedTools: [], MaximumToolRounds: 0,
        MaximumOutputTokens: 300, MaximumToolCalls: 0);

    private sealed class RetainedReviewProvider(
        ApplicationCandidateReuseInputV2 input, string judgment = "uncertain") : IAiProvider
    {
        public int Calls { get; private set; }
        public AiProviderInfo Info => new("host-provider", "Controlled reviewer fixture");
        public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiModel>>([]);
        public Task<AiProviderResponse> SendAsync(AiProviderRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Empty(request.Tools);
            var json = JsonSerializer.Serialize(new
            {
                format = ApplicationCandidateReuseJudgmentOutputV2.OutputDomain,
                input.SelectionFingerprint, input.InputFingerprint, input.ManualResultFingerprint,
                judgment, reason = "The retained evidence supports the bounded fixture judgment.",
                assessments = input.Alternatives.Select(target => new
                { target, judgment, reason = "The exact authorized alternative was assessed." })
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Task.FromResult(new AiProviderResponse(true, null, "review", json, [],
                Usage: new(7, 2, 9, true)));
        }
    }

    private static ApplicationCandidatePureRuntimeClosureReader PureRuntimeClosure(DantesRoleplayDbContext db, SetupState setup) =>
        new(db, setup.Applications, setup.Activation, setup.Resolver, new(new BoundedJsonSchemaValidator()));

    private static ApplicationCandidateRuntimeValidator PureRuntimeValidator(DantesRoleplayDbContext db, SetupState setup, JintMechanicEngine? engine = null) =>
        new(PureRuntimeClosure(db, setup), engine ?? new JintMechanicEngine(), new BoundedJsonSchemaValidator(),
            setup.Resolver, new SqliteStandingGrantPolicy(db, setup.Resolver));

    private static async Task SeedPureRuntimeGrantAsync(DantesRoleplayDbContext db)
    {
        var grant = new StandingGrantRevision("grant@1", "grant", 1, new string('0', 64),
            "principal." + new string('a', 64), Application, StandingGrantScope.Application, null,
            [StandingGrantCapability.Author, StandingGrantCapability.Read, StandingGrantCapability.Validate],
            new(StandingGrantDefinitionMode.ApplicationOwned, [], [new("demo.runtime", true, [CatalogNamespaceKinds.Mechanic])]),
            [], 16, DateTime.UtcNow.AddMinutes(10), false, "pure-grant-operation");
        db.Add(new Operation { Id = grant.IssuedByOperationId, Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord
        {
            GrantId = grant.GrantId, Revision = grant.Revision, GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference, ApplicationId = Application.Value, Scope = "application",
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant),
            MaximumOperations = grant.MaximumOperations, ExpiresAtUtc = grant.ExpiresAtUtc,
            IssuedByOperationId = grant.IssuedByOperationId
        });
        db.Add(new StandingGrantCurrentRecord { GrantId = grant.GrantId, Revision = 1 });
        await db.SaveChangesAsync();
    }
}
