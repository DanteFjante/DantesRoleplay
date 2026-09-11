using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task One_file_authoring_and_recovery_preserve_a_large_generation_and_inspect_only_changed_content()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        for (var index = 0; index < 220; index++)
            File.WriteAllText(Path.Combine(root, "content", "procedures", $"unchanged-{index}.md"),
                ProcedureText("Unchanged fixture.").Replace("demo.runtime.inspect", $"demo.runtime.unchanged-{index}", StringComparison.Ordinal),
                new UTF8Encoding(false));
        await ActivateAsync(setup);
        var historical = setup.Activation.Current(Application)!;
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Read, StandingGrantCapability.Validate]);
        var service = Service(db, setup);
        var write = await service.WriteCandidateAsync(Host(setup, "large-write", InteractionExecutionProfile.Atomic),
            Request(historical.ActivationFingerprint));
        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
        var metadata = await new ApplicationCandidateRetainedReader(db, setup.Applications)
            .ReadMetadataAsync(Application, row.CandidateId, row.Revision);
        Assert.Equal(221, metadata!.Documents.Count);
        var inspection = await service.InspectAsync(Host(setup, "large-inspect"), new(null, 0, write.Receipt!.OperationId));
        Assert.Equal(InteractionInvocationResultTag.Completed, inspection.Tag);
        using var inspected = JsonDocument.Parse(inspection.DataJson!);
        Assert.Equal(1, inspected.RootElement.GetProperty("documents").GetArrayLength());
        Assert.DoesNotContain("Unchanged fixture", inspection.DataJson);

        WriteProcedure("A later active change.");
        await ActivateAsync(setup, historical.ActivationFingerprint);
        var current = setup.Activation.Current(Application)!;
        var recovery = await service.RecoverAsync(Host(setup, "large-recovery", InteractionExecutionProfile.Atomic),
            historical.ActivationRevision, current.ActivationFingerprint);
        Assert.Equal(InteractionInvocationResultTag.Committed, recovery.Tag);
        var recovered = await service.InspectAsync(Host(setup, "large-recovery-inspect"), new(null, 0, recovery.Receipt!.OperationId));
        Assert.Equal(InteractionInvocationResultTag.Completed, recovered.Tag);
        using var recoveredJson = JsonDocument.Parse(recovered.DataJson!);
        Assert.Equal(1, recoveredJson.RootElement.GetProperty("documents").GetArrayLength());
        Assert.Contains("Inspect it.", recovered.DataJson);
        Assert.Equal(current.ActivationFingerprint, setup.Activation.Current(Application)!.ActivationFingerprint);
    }
}
