using System.Text;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    private const string PureActionId = "demo.runtime.pure.calculate";
    private const string PureActionMarkdownPath = "content/mechanics/calculate.md";
    private const string PureActionJavaScriptPath = "content/mechanics/calculate.js";

    [Fact]
    public async Task Application_only_action_executes_exact_active_pure_mechanic_with_process_local_evidence()
    {
        await using var db = fixture.CreateContext();
        var setup = await PureActionSetupAsync(db,
            "return { data: { answer: ctx.input.value + 1 } };",
            "{\"inputSchema\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}}");
        var (adapter, record) = PureActionAdapter(db, setup);
        var host = PureActionHost(setup);
        var operationsBefore = await db.Operations.CountAsync();

        var result = await adapter.ExecuteAsync(new(host, record.QualifiedId,
            record.Version, record.ContentFingerprint, new Dictionary<string, string>(), "{ \"value\" : 4 }"));

        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        Assert.Equal("{\"answer\":5}", result.DataJson);
        Assert.Null(result.Receipt);
        Assert.Null(result.ReadEvidence);
        Assert.StartsWith("pure-action-process-local.", result.CompletionEvidenceReference, StringComparison.Ordinal);
        Assert.Equal(0, host.Budget.RemainingOperations);
        Assert.Equal(operationsBefore, await db.Operations.CountAsync());
        Assert.Null(db.Database.CurrentTransaction);

        var invalidHost = PureActionHost(setup);
        var invalid = await adapter.ExecuteAsync(new(invalidHost, record.QualifiedId,
            record.Version, record.ContentFingerprint, new Dictionary<string, string>(),
            "{\"value\":\"four\"}"));
        Assert.Equal(InteractionInvocationResultTag.Failed, invalid.Tag);
        Assert.Equal("PURE_ACTION_INPUT_INVALID", invalid.Code);
        Assert.Equal(1, invalidHost.Budget.RemainingOperations);
    }

    [Fact]
    public async Task Application_only_action_denial_and_revocation_do_not_execute_or_spend_budget()
    {
        await using var db = fixture.CreateContext();
        var setup = await PureActionSetupAsync(db, "return { data: { executed: true } };");
        var (adapter, record) = PureActionAdapter(db, setup);
        var wrongGrant = PureActionHost(setup, grantReference: "missing@1");

        var denied = await adapter.ExecuteAsync(Request(wrongGrant, record));
        await RevokeGrantAsync(db);
        db.ChangeTracker.Clear();
        var revokedHost = PureActionHost(setup);
        var revoked = await adapter.ExecuteAsync(Request(revokedHost, record));

        Assert.Equal(InteractionInvocationResultTag.Failed, denied.Tag);
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", denied.Code);
        Assert.Equal(1, wrongGrant.Budget.RemainingOperations);
        Assert.Equal(InteractionInvocationResultTag.Failed, revoked.Tag);
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", revoked.Code);
        Assert.Equal(1, revokedHost.Budget.RemainingOperations);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Application_only_action_requires_read_and_execute_on_the_same_current_grant()
    {
        await using var db = fixture.CreateContext();
        var setup = await PureActionSetupAsync(db, "return { data: { executed: true } };");
        var (adapter, record) = PureActionAdapter(db, setup);
        await ReplaceGrantCapabilitiesAsync(db, [StandingGrantCapability.Execute]);
        db.ChangeTracker.Clear();
        var host = PureActionHost(setup, "grant@2");

        var result = await adapter.ExecuteAsync(Request(host, record));

        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal("INVOCATION_NOT_AUTHORIZED", result.Code);
        Assert.Equal(1, host.Budget.RemainingOperations);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Published_replacement_rejects_the_old_exact_selection()
    {
        await using var db = fixture.CreateContext();
        var setup = await PureActionSetupAsync(db, "return { data: { revision: 1 } };");
        var (adapter, oldRecord) = PureActionAdapter(db, setup);
        var oldActivation = setup.Activation.Current(Application)!.ActivationFingerprint;
        WritePureAction("return { data: { revision: 2 } };", "{}");
        await ActivateAsync(setup, oldActivation);

        var staleHost = PureActionHost(setup);
        var stale = await adapter.ExecuteAsync(Request(staleHost, oldRecord));

        Assert.Equal(InteractionInvocationResultTag.Failed, stale.Tag);
        Assert.Equal("PURE_ACTION_SELECTION_STALE", stale.Code);
        Assert.Equal(1, staleHost.Budget.RemainingOperations);
    }

    [Theory]
    [InlineData("{\"roles\":{\"subject\":{}}}", "return { data: {} };")]
    [InlineData("{}", "return { data: {}, effects: [{ type: 'invented' }] };")]
    public async Task Application_only_action_rejects_nonpure_contracts_and_outputs(
        string requirements,
        string source)
    {
        await using var db = fixture.CreateContext();
        var setup = await PureActionSetupAsync(db, source, requirements);
        var (adapter, record) = PureActionAdapter(db, setup);
        var host = PureActionHost(setup);

        var result = await adapter.ExecuteAsync(Request(host, record));

        if (requirements != "{}")
        {
            Assert.Equal(InteractionInvocationResultTag.Unavailable, result.Tag);
            Assert.Equal("PURE_MECHANIC_REQUIREMENTS_UNAVAILABLE", result.Code);
            Assert.Equal(1, host.Budget.RemainingOperations);
        }
        else
        {
            Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
            Assert.Equal("PURE_ACTION_OUTPUT_INVALID", result.Code);
            Assert.Equal(0, host.Budget.RemainingOperations);
        }
        Assert.Null(result.Receipt);
    }

    private async Task<SetupState> PureActionSetupAsync(
        DantesRoleplayDbContext db,
        string source,
        string requirements = "{}")
    {
        var setup = Setup(db);
        setup.Namespaces.Register(new CatalogNamespaceRegistration(
            "demo.runtime.pure", "human-domain-label", "Pure action fixtures.",
            [CatalogNamespaceKinds.Mechanic],
            ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed pure action fixture."));
        WritePureAction(source, requirements);
        await ActivateAsync(setup);
        await SeedPureActionGrantAsync(db);
        return setup;
    }

    private void WritePureAction(string source, string requirements)
    {
        var directory = Path.Combine(root, "content", "mechanics");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(root,
            PureActionMarkdownPath.Replace('/', Path.DirectorySeparatorChar)), $$"""
            ---
            id: {{PureActionId}}
            category: runtime.pure
            name: Calculate
            status: active
            ---

            ## Description
            Calculate a state-free result.

            ## Requirements
            ```json
            {{requirements}}
            ```
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root,
            PureActionJavaScriptPath.Replace('/', Path.DirectorySeparatorChar)),
            source, new UTF8Encoding(false));
    }

    private static (ApplicationActionInvocationAdapter Adapter, CatalogRecordSummary Record)
        PureActionAdapter(DantesRoleplayDbContext db, SetupState setup)
    {
        var materializer = new ActivatedApplicationCatalogMaterializer(
            setup.Applications, setup.Activation, setup.Sources, setup.Roots, setup.Extensions)
            .UsePreparationCache(new ActivatedApplicationCatalogSnapshotCache(),
                new ActivatedApplicationCatalogCacheAuthority());
        var catalogs = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([Application.Value]),
            materializer,
            new CatalogCursorCodec(Enumerable.Repeat((byte)0x42, 32).ToArray()),
            setup.Activation);
        Assert.True(catalogs.TryGet(Application, out var catalog));
        var record = catalog.Inspect(new(Application, Application.Value, PureActionId)).Summary;
        var schemas = new BoundedJsonSchemaValidator();
        var executor = new ApplicationPureActionExecutor(
            catalogs, new(schemas), new JintMechanicEngine(), schemas,
            setup.Resolver, new SqliteStandingGrantPolicy(db, setup.Resolver),
            new SqliteEcsWriteTransactionFactory(db));
        return (new ApplicationActionInvocationAdapter(
            null!, null!, null!, new OperationLog(db), executor), record);
    }

    private static ApplicationActionInvocationRequest Request(
        InteractionInvocationHost host,
        CatalogRecordSummary record) => new(
        host, record.QualifiedId, record.Version, record.ContentFingerprint,
        new Dictionary<string, string>(), "{}");

    private static InteractionInvocationHost PureActionHost(
        SetupState setup,
        string grantReference = "grant@1") => InteractionInvocationHost.ForApplication(
        TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
        new(Application, 1, setup.Applications.Get(Application)!.Fingerprint, []),
        grantReference, "pure-action", InteractionExecutionProfile.Atomic,
        new(1, DateTime.UtcNow.AddMinutes(1)));

    private static async Task SeedPureActionGrantAsync(DantesRoleplayDbContext db)
    {
        var grant = new StandingGrantRevision(
            "grant@1", "grant", 1, new string('0', 64),
            "principal." + new string('a', 64), Application,
            StandingGrantScope.Application, null,
            [StandingGrantCapability.Read, StandingGrantCapability.Execute],
            new(StandingGrantDefinitionMode.ApplicationOwned, [],
                [new("demo.runtime.pure", false, [CatalogNamespaceKinds.Mechanic])]),
            [], 1, DateTime.UtcNow.AddMinutes(10), false, "pure-action-grant-operation");
        db.Add(new Operation
        {
            Id = grant.IssuedByOperationId,
            Timestamp = DateTime.UtcNow,
            Tool = "test"
        });
        db.Add(new StandingGrantRevisionRecord
        {
            GrantId = grant.GrantId,
            Revision = grant.Revision,
            GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference,
            ApplicationId = Application.Value,
            Scope = "application",
            PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant),
            MaximumOperations = grant.MaximumOperations,
            ExpiresAtUtc = grant.ExpiresAtUtc,
            IssuedByOperationId = grant.IssuedByOperationId
        });
        db.Add(new StandingGrantCurrentRecord { GrantId = grant.GrantId, Revision = 1 });
        await db.SaveChangesAsync();
    }
}
