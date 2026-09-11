using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Recovery_creates_a_fresh_inert_candidate_from_a_historical_retained_generation_and_replays_its_receipt()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        var historical = setup.Activation.Current(Application)!;
        WriteProcedure("Current generation.");
        await ActivateAsync(setup, historical.ActivationFingerprint);
        var current = setup.Activation.Current(Application)!;
        await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var service = Service(db, setup);

        var recovered = await service.RecoverAsync(ApplicationHost(setup, "recover", InteractionExecutionProfile.Atomic),
            historical.ActivationRevision, current.ActivationFingerprint);
        var stored = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        var readback = await new ApplicationCandidateRetainedReader(db, setup.Applications)
            .ReadAsync(Application, stored.CandidateId, stored.Revision);

        Assert.Equal(InteractionInvocationResultTag.Committed, recovered.Tag);
        Assert.Equal(historical.ActivationFingerprint, setup.Activation.ReadRevision(Application, historical.ActivationRevision)!.ActivationFingerprint);
        Assert.Equal(current.ActivationFingerprint, setup.Activation.Current(Application)!.ActivationFingerprint);
        Assert.Equal(ProcedureText("Inspect it."), Encoding.UTF8.GetString(Assert.Single(readback!.Documents).RetainedBytes));

        WriteProcedure("A later current generation.");
        await ActivateAsync(setup, current.ActivationFingerprint);
        var replay = await service.RecoverAsync(ApplicationHost(setup, "recover", InteractionExecutionProfile.Atomic),
            historical.ActivationRevision, current.ActivationFingerprint);

        Assert.Equal(recovered.Receipt, replay.Receipt);
        Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Recovery_refuses_a_historical_generation_that_would_remove_a_current_document()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        var historical = setup.Activation.Current(Application)!;
        var extraPath = Path.Combine(root, "content", "procedures", "extra.md");
        Directory.CreateDirectory(Path.GetDirectoryName(extraPath)!);
        File.WriteAllText(extraPath, ProcedureText("Extra current document.")
            .Replace("demo.runtime.inspect", "demo.runtime.extra", StringComparison.Ordinal), new UTF8Encoding(false));
        await ActivateAsync(setup, historical.ActivationFingerprint);
        var current = setup.Activation.Current(Application)!;
        await SeedGrantAsync(db, [StandingGrantCapability.Author]);

        var recovered = await Service(db, setup).RecoverAsync(ApplicationHost(setup, "recover-extra", InteractionExecutionProfile.Atomic),
            historical.ActivationRevision, current.ActivationFingerprint);

        Assert.Equal(InteractionInvocationResultTag.Unavailable, recovered.Tag);
        Assert.Equal("APPLICATION_CANDIDATE_RECOVER_UNAVAILABLE", recovered.Code);
        Assert.Empty(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        Assert.Empty(await db.Operations.Where(value => value.Tool == "application-candidate").ToArrayAsync());
        Assert.Equal(current.ActivationFingerprint, setup.Activation.Current(Application)!.ActivationFingerprint);
    }

    [Fact]
    public async Task Recovery_requires_exact_current_active_fingerprint_and_retained_historical_bytes()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        var historical = setup.Activation.Current(Application)!;
        WriteProcedure("Current generation.");
        await ActivateAsync(setup, historical.ActivationFingerprint);
        var current = setup.Activation.Current(Application)!;
        await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var service = Service(db, setup);

        var stale = await service.RecoverAsync(ApplicationHost(setup, "recover-stale", InteractionExecutionProfile.Atomic),
            historical.ActivationRevision, new string('A', 64));
        var link = await db.Set<ApplicationActivationDocumentRecord>().SingleAsync(value =>
            value.ActivationRevision == historical.ActivationRevision);
        var evidence = await db.Set<ApplicationActivationDocumentEvidenceRecord>().SingleAsync(value =>
            value.IdentityId == link.IdentityId && value.EvidenceVersion == link.EvidenceVersion);
        evidence.RetainedBytes = null;
        await db.SaveChangesAsync();
        var missing = await service.RecoverAsync(ApplicationHost(setup, "recover-missing", InteractionExecutionProfile.Atomic),
            historical.ActivationRevision, current.ActivationFingerprint);

        Assert.Equal(InteractionInvocationResultTag.Failed, stale.Tag);
        Assert.Equal("APPLICATION_CANDIDATE_ACTIVE_STALE", stale.Code);
        Assert.Equal(InteractionInvocationResultTag.Unavailable, missing.Tag);
        Assert.Equal("APPLICATION_CANDIDATE_RECOVER_UNAVAILABLE", missing.Code);
        Assert.Empty(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        Assert.Empty(await db.Operations.Where(value => value.Tool == "application-candidate").ToArrayAsync());
    }
}
