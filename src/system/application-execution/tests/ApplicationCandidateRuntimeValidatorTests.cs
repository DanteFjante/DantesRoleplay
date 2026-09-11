using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

// Reuse the actual SQLite authoring/activation/standing-grant fixture. Runtime evidence is never
// supplied by a substitute closure reader, preparation adapter or authorization policy.
public sealed partial class SqliteStandingGrantTargetResolverTests
{
    private const string PureMarkdownPath = "content/mechanics/pure.md";
    private const string PureJavaScriptPath = "content/mechanics/pure.js";

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
