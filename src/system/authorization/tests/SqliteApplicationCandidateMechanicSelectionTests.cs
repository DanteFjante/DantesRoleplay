using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using DantesRoleplay.Sources;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    private const string MechanicMarkdownPath = "content/mechanics/check/mechanic.fixture.check.md";
    private const string MechanicSourcePath = "content/mechanics/check/mechanic.fixture.check.js";
    private const string MechanicId = "demo.runtime.mechanics.check";

    [Fact]
    public async Task Candidate_selection_for_a_javascript_only_mechanic_edit_retains_its_contract_and_pins_the_new_normalized_hash()
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupMechanicAsync(db);
        var active = setup.Activation.Current(Application)!;
        var activeTarget = Assert.IsType<StandingGrantDefinitionTarget>((await setup.Resolver.ResolveCurrentAsync(
            ApplicationHost(setup, "mechanic-active"), MechanicId, CatalogNamespaceKinds.Mechanic)).Target);
        var write = await Service(db, setup).WriteCandidateAsync(ApplicationHost(setup, "mechanic-js", InteractionExecutionProfile.Atomic,
            "mechanic-grant@1"), MechanicRequest(active.ActivationFingerprint,
                new ApplicationCandidateDocumentInput("file:" + MechanicSourcePath, "catalog", MechanicSourcePath, "text/javascript", MechanicSource("v2"))));
        var candidate = await CandidateAsync(db);

        var selected = await new ApplicationCandidateSelectionReader(db, setup.Applications, setup.Activation, setup.Resolver)
            .ReadAsync(ApplicationHost(setup, "mechanic-js-select", grantReference: "mechanic-grant@1"), candidate);

        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        Assert.NotNull(selected);
        Assert.False(selected!.DependenciesComplete);
        Assert.Equal("changed-definitions-mechanic-sidecars-v1", selected.CoverageVersion);
        Assert.Equal(MechanicSourcePath, Assert.Single(selected.ChangedPaths));
        Assert.Equal([MechanicSourcePath, MechanicMarkdownPath], selected.Documents.Select(value => value.Document.RelativePath).ToArray());
        Assert.Equal(["changed", "sidecar"], selected.Documents.Select(value => value.Role).ToArray());
        var target = Assert.Single(selected.Targets);
        Assert.Equal(MechanicId, target.DefinitionId);
        Assert.NotEqual(activeTarget.ContentFingerprint, target.ContentFingerprint);
        Assert.All(selected.Documents, document => Assert.Equal(new StandingGrantDefinitionReference(
            target.DefinitionId, target.Kind, target.Revision, target.ContentFingerprint), document.Definition));
    }

    [Fact]
    public async Task Candidate_selection_for_a_contract_only_mechanic_edit_retains_the_actual_javascript_sidecar()
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupMechanicAsync(db);
        var active = setup.Activation.Current(Application)!;
        var write = await Service(db, setup).WriteCandidateAsync(ApplicationHost(setup, "mechanic-contract", InteractionExecutionProfile.Atomic,
            "mechanic-grant@1"), MechanicRequest(active.ActivationFingerprint,
                new ApplicationCandidateDocumentInput("file:" + MechanicMarkdownPath, "catalog", MechanicMarkdownPath, "text/markdown", MechanicMarkdown("Updated check fixture"))));
        var candidate = await CandidateAsync(db);

        var selected = await new ApplicationCandidateSelectionReader(db, setup.Applications, setup.Activation, setup.Resolver)
            .ReadAsync(ApplicationHost(setup, "mechanic-contract-select", grantReference: "mechanic-grant@1"), candidate);

        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        Assert.NotNull(selected);
        Assert.Equal(MechanicMarkdownPath, Assert.Single(selected!.ChangedPaths));
        var sidecar = Assert.Single(selected.Documents.Where(value => value.Role == "sidecar"));
        Assert.Equal(MechanicSourcePath, sidecar.Document.RelativePath);
        Assert.Equal(MechanicSource("v1"), Encoding.UTF8.GetString(sidecar.RetainedBytes.AsSpan()));
        var target = Assert.Single(selected.Targets);
        Assert.All(selected.Documents, document => Assert.Equal(new StandingGrantDefinitionReference(
            target.DefinitionId, target.Kind, target.Revision, target.ContentFingerprint), document.Definition));
    }

    [Fact]
    public async Task Candidate_authoring_fails_closed_when_a_new_mechanic_is_missing_or_splits_its_javascript_sidecar()
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupMechanicAsync(db);
        setup.Sources.Register(new(Application, "other", "fixture-root", "content/**/*", SourceTrust.Trusted, -1, "other-source"));
        var service = Service(db, setup);
        var active = setup.Activation.Current(Application)!;
        var missing = await service.WriteCandidateAsync(ApplicationHost(setup, "mechanic-missing", InteractionExecutionProfile.Atomic,
            "mechanic-grant@1"), MechanicRequest(active.ActivationFingerprint,
                new ApplicationCandidateDocumentInput("file:content/mechanics/new/missing.md", "catalog", "content/mechanics/new/missing.md", "text/markdown", MechanicMarkdown("Missing sidecar", "demo.runtime.mechanics.missing"))));
        var split = await service.WriteCandidateAsync(ApplicationHost(setup, "mechanic-split", InteractionExecutionProfile.Atomic,
            "mechanic-grant@1"), MechanicRequest(active.ActivationFingerprint,
                new ApplicationCandidateDocumentInput("file:content/mechanics/new/split.md", "catalog", "content/mechanics/new/split.md", "text/markdown", MechanicMarkdown("Split sidecar", "demo.runtime.mechanics.split")),
                new ApplicationCandidateDocumentInput("file:content/mechanics/new/split.js", "other", "content/mechanics/new/split.js", "text/javascript", MechanicSource("split"))));

        Assert.Equal(InteractionInvocationResultTag.Failed, missing.Tag);
        Assert.Equal(InteractionInvocationResultTag.Failed, split.Tag);
        Assert.Empty(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
    }

    private async Task<SetupState> SetupMechanicAsync(DantesRoleplayDbContext db)
    {
        var setup = Setup(db);
        setup.Namespaces.Register(new CatalogNamespaceRegistration("demo.runtime.mechanics", "human-domain-label",
            "Mechanic fixture namespace.", [CatalogNamespaceKinds.Mechanic], ReviewStatus: CatalogNamespaceReviewStatuses.Reviewed,
            ReviewNote: "Reviewed mechanic fixture."));
        WriteMechanic(MechanicMarkdown(), MechanicSource("v1"));
        await ActivateAsync(setup);
        await SeedMechanicGrantAsync(db);
        return setup;
    }

    private async Task<ApplicationCandidateReference> CandidateAsync(DantesRoleplayDbContext db)
    {
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().ToArrayAsync());
        return new(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
    }

    private static ApplicationCandidateWriteRequest MechanicRequest(string activeFingerprint,
        params ApplicationCandidateDocumentInput[] documents) => new(null, 0, activeFingerprint, "runtime", null,
            "Mechanic fixture change.", documents);

    private void WriteMechanic(string markdown, string source)
    {
        var markdownPath = Path.Combine(root, MechanicMarkdownPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);
        File.WriteAllText(markdownPath, markdown, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, MechanicSourcePath.Replace('/', Path.DirectorySeparatorChar)), source, new UTF8Encoding(false));
    }

    private static string MechanicMarkdown(string name = "Check fixture", string id = MechanicId) => $$"""
        ---
        id: {{id}}
        category: fixture.check
        name: {{name}}
        scope: action
        status: active
        ---

        ## Description
        Check a fixture.

        ## Matches
        check fixture

        ## Requirements
        ```json
        {}
        ```
        """;

    private static string MechanicSource(string value) => "return { narration: '" + value + "', effects: [] };";

    private static async Task SeedMechanicGrantAsync(DantesRoleplayDbContext db)
    {
        var grant = new StandingGrantRevision("mechanic-grant@1", "mechanic-grant", 1, new string('0', 64),
            "principal." + new string('a', 64), Application, StandingGrantScope.Application, null,
            [StandingGrantCapability.Author], new(StandingGrantDefinitionMode.ApplicationOwned, [],
                [new("demo.runtime.mechanics", true, [CatalogNamespaceKinds.Mechanic])]), [], 1,
            DateTime.UtcNow.AddMinutes(10), false, "mechanic-grant-operation");
        db.Add(new Operation { Id = "mechanic-grant-operation", Timestamp = DateTime.UtcNow, Tool = "test" });
        db.Add(new StandingGrantRevisionRecord
        {
            GrantId = grant.GrantId, Revision = grant.Revision, GrantReference = grant.GrantReference,
            PrincipalReference = grant.PrincipalReference, ApplicationId = grant.ApplicationId.Value, Scope = "application",
            StateSpaceId = null, PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
            ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant), Revoked = false,
            ExpiresAtUtc = grant.ExpiresAtUtc, MaximumOperations = grant.MaximumOperations,
            IssuedByOperationId = grant.IssuedByOperationId
        });
        db.Add(new StandingGrantCurrentRecord { GrantId = grant.GrantId, Revision = grant.Revision });
        await db.SaveChangesAsync();
    }
}
