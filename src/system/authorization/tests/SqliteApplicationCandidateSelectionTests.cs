using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Candidate_selection_pins_exact_base_and_only_changed_content_without_claiming_permission_or_closure()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        var basis = setup.Activation.Current(Application)!;
        await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var write = await Service(db, setup).WriteCandidateAsync(ApplicationHost(setup, "selection-write", InteractionExecutionProfile.Atomic),
            Request(basis.ActivationFingerprint));
        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().ToArrayAsync());
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
        var reader = new ApplicationCandidateSelectionReader(db, setup.Applications, setup.Activation, setup.Resolver);
        // Selection proves content/ownership only: no permission is inferred from this absent grant.
        var host = ApplicationHost(setup, "selection", grantReference: "absent@1");
        var before = host.Budget.RemainingOperations;
        var selected = await reader.ReadAsync(host, candidate);

        Assert.NotNull(selected);
        Assert.False(selected.DependenciesComplete);
        Assert.Equal("changed-definitions-mechanic-sidecars-v1", selected.CoverageVersion);
        Assert.Equal(candidate, selected.Candidate);
        Assert.Equal(basis.ActivationFingerprint, selected.BaseOrigin!.ActivationFingerprint);
        Assert.Equal(RelativePath, Assert.Single(selected.ChangedPaths));
        var document = Assert.Single(selected.Documents);
        Assert.Equal("changed", document.Role);
        Assert.Contains("Changed by candidate", Encoding.UTF8.GetString(document.RetainedBytes.AsSpan()));
        Assert.Single(selected.Targets);
        Assert.Equal(before, host.Budget.RemainingOperations);
        var replay = await reader.ReadAsync(ApplicationHost(setup, "selection-again"), candidate);
        Assert.Equal(selected.EvidenceFingerprint, replay!.EvidenceFingerprint);
        Assert.Null(await reader.ReadAsync(ApplicationHost(setup), candidate with { ContentFingerprint = new string('A', 64) }));
        setup.Namespaces.SetEnabled("demo.runtime", false);
        Assert.Null(await reader.ReadAsync(ApplicationHost(setup), candidate));
    }
}
