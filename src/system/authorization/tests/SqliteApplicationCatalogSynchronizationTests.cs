using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Events;
using DantesRoleplay.Information;
using DantesRoleplay.Interactions;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.Sources;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public void Production_registration_shares_the_catalog_synchronization_owner_with_candidate_admission()
    {
        var services = new ServiceCollection();
        services.AddDantesRoleplayDataAccess("Filename=:memory:");
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var owner = scope.ServiceProvider.GetRequiredService<CatalogSynchronizationService>();
        Assert.Same(owner, scope.ServiceProvider.GetRequiredService<ICatalogSynchronizationService>());
        Assert.Same(owner, scope.ServiceProvider.GetRequiredService<IApplicationCatalogSynchronizationEvidenceReader>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IApplicationAuthoringService>());
    }

    [Fact]
    public async Task Catalog_comparison_receipt_replays_and_admits_only_the_exact_reviewed_candidate()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Read]);
        var prepared = await PrepareSynchronizationAsync(db, setup, "Reviewed catalog edit.");
        var host = ApplicationHost(setup, "catalog-compare");
        var comparison = await prepared.Service.CompareAsync(host, prepared.CompareRequest);
        var replay = await prepared.Service.CompareAsync(ApplicationHost(setup, "catalog-compare"), prepared.CompareRequest);
        var reference = EvidenceReference(comparison);
        var request = Candidate(prepared, reference);
        var authoring = Service(db, setup, synchronization: prepared.Service);

        var written = await authoring.WriteCandidateAsync(
            ApplicationHost(setup, "catalog-candidate", InteractionExecutionProfile.Atomic), request);
        await File.AppendAllTextAsync(prepared.Path, "\nChanged after the committed candidate.\n");
        var writeReplay = await authoring.WriteCandidateAsync(
            ApplicationHost(setup, "catalog-candidate", InteractionExecutionProfile.Atomic), request);

        Assert.Equal(InteractionInvocationResultTag.Completed, comparison.Tag);
        Assert.Equal(comparison.DataJson, replay.DataJson);
        Assert.Equal(InteractionInvocationResultTag.Committed, written.Tag);
        Assert.Equal(InteractionInvocationResultTag.Committed, writeReplay.Tag);
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().ToArrayAsync());
        Assert.Equal("catalog-sync", row.Origin);
        Assert.Equal(reference, row.SynchronizationEvidenceReference);
        Assert.Single(await db.Operations.Where(value => value.Tool == "catalog-synchronization-compare").ToArrayAsync());
        Assert.Single(await db.Operations.Where(value => value.Tool == "application-candidate").ToArrayAsync());
    }

    [Fact]
    public async Task Catalog_candidate_rejects_bytes_that_differ_from_the_reviewed_selection()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var prepared = await PrepareSynchronizationAsync(db, setup, "Reviewed catalog edit.");
        var comparison = await prepared.Service.CompareAsync(
            ApplicationHost(setup, "catalog-byte-compare"), prepared.CompareRequest);
        var request = Candidate(prepared, EvidenceReference(comparison));
        request = request with { Documents = [request.Documents[0] with { Text = request.Documents[0].Text + "\nChanged after review." }] };

        var result = await Service(db, setup, synchronization: prepared.Service).WriteCandidateAsync(
            ApplicationHost(setup, "catalog-byte-write", InteractionExecutionProfile.Atomic), request);

        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal("APPLICATION_CANDIDATE_SYNC_DOCUMENT_MISMATCH", result.Code);
        Assert.Empty(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Catalog_candidate_rechecks_and_rejects_a_stale_comparison()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var prepared = await PrepareSynchronizationAsync(db, setup, "Reviewed catalog edit.");
        var comparison = await prepared.Service.CompareAsync(
            ApplicationHost(setup, "catalog-stale-compare"), prepared.CompareRequest);
        var request = Candidate(prepared, EvidenceReference(comparison));
        await File.AppendAllTextAsync(prepared.Path, "\nChanged after comparison.\n");

        var result = await Service(db, setup, synchronization: prepared.Service).WriteCandidateAsync(
            ApplicationHost(setup, "catalog-stale-write", InteractionExecutionProfile.Atomic), request);

        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal("APPLICATION_CANDIDATE_SYNC_EVIDENCE_STALE", result.Code);
        Assert.Empty(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
    }

    [Fact]
    public async Task Catalog_conflict_receipt_preserves_both_versions_and_cannot_admit_a_candidate()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var prepared = await PrepareSynchronizationAsync(db, setup, "File-side version.");
        var beforeFile = await File.ReadAllTextAsync(prepared.Path);
        var beforeVersion = (await new ProcedureStore(db).GetAsync("demo.runtime.inspect"))!.Version;
        await WriteStoredProcedureAsync(db, "Database-side version.");
        var databaseVersion = (await new ProcedureStore(db).GetAsync("demo.runtime.inspect"))!.Version;

        var comparison = await prepared.Service.CompareAsync(
            ApplicationHost(setup, "catalog-conflict-compare"), prepared.CompareRequest);
        var reference = EvidenceReference(comparison);
        var operation = await db.Operations.AsNoTracking().SingleAsync(value => value.Id == reference);
        using var evidence = JsonDocument.Parse(operation.GuardEvidenceJson);
        var recordEvidence = evidence.RootElement.GetProperty("Records")[0];
        var result = await Service(db, setup, synchronization: prepared.Service).WriteCandidateAsync(
            ApplicationHost(setup, "catalog-conflict-write", InteractionExecutionProfile.Atomic),
            Candidate(prepared, reference));

        Assert.Equal("conflict", JsonDocument.Parse(comparison.DataJson!).RootElement.GetProperty("status").GetString());
        Assert.Equal(InteractionInvocationResultTag.Failed, result.Tag);
        Assert.Equal("APPLICATION_CANDIDATE_SYNC_CONFLICT", result.Code);
        Assert.NotEqual(recordEvidence.GetProperty("FileFingerprint").GetString(),
            recordEvidence.GetProperty("DatabaseFingerprint").GetString());
        Assert.NotEqual(recordEvidence.GetProperty("AncestorFingerprint").GetString(),
            recordEvidence.GetProperty("FileFingerprint").GetString());
        Assert.NotEqual(recordEvidence.GetProperty("AncestorFingerprint").GetString(),
            recordEvidence.GetProperty("DatabaseFingerprint").GetString());
        Assert.Equal(beforeFile, await File.ReadAllTextAsync(prepared.Path));
        Assert.Equal(beforeVersion + 1, databaseVersion);
        Assert.Equal(databaseVersion, (await new ProcedureStore(db).GetAsync("demo.runtime.inspect"))!.Version);
        Assert.Empty(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());
    }

    private async Task<SynchronizationFixture> PrepareSynchronizationAsync(
        DantesRoleplayDbContext db, SetupState setup, string fileInstruction)
    {
        await WriteStoredProcedureAsync(db, "Inspect it.");
        var synchronizationRoot = Path.Combine(root, "synchronization");
        await new CatalogExporter(db).ExportAsync(synchronizationRoot, new CatalogExportOptions(RulesOnly: true));
        var path = CatalogLayout.ToFileSystemPath(synchronizationRoot,
            CatalogLayout.ProcedureMarkdown("", "demo.runtime.inspect"));
        var text = (await File.ReadAllTextAsync(path)).Replace("Inspect it.", fileInstruction, StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, text);
        setup.Sources.Register(new(Application, "synchronization-catalog", "synchronization-root",
            "procedures/**/*.md", SourceTrust.Trusted, 10, "synchronization-catalog"));
        var roots = new SynchronizationRoots(synchronizationRoot);
        var importer = new CatalogImporter(db, new MechanicStore(db), new ProcedureStore(db), new WorldStore(db),
            new EventTypeStore(db), new SubscriptionStore(db));
        var service = new CatalogSynchronizationService(importer, setup.Applications, setup.Activation, setup.Sources,
            roots, new RegisteredSourceScanner(setup.Sources, roots, new LocalDocumentScanner()),
            new SourceOverlayResolver(), new OperationLog(db));
        var request = new CatalogSynchronizationCompareRequest("synchronization-root",
            setup.Activation.Current(Application)!.ActivationFingerprint,
            [new(CatalogRecordKind.Procedure, "demo.runtime.inspect")]);
        return new(service, request, path, text, CatalogLayout.ProcedureMarkdown("", "demo.runtime.inspect"));
    }

    private static async Task WriteStoredProcedureAsync(DantesRoleplayDbContext db, string instruction)
    {
        await new ProcedureStore(db).WriteAsync(new WriteProcedureRequest
        {
            Id = "demo.runtime.inspect", Category = "runtime.inspect", Name = "Inspect runtime",
            Description = "Inspect the active runtime definition.", Governs = "query(kind: \"runtime.inspect\")",
            Instructions = "1. " + instruction, Constraints = "- Preserve it.",
            Status = ProcedureStatus.Active, CreatedBy = "test", ChangeNote = "Synchronization fixture."
        });
    }

    private static ApplicationCandidateWriteRequest Candidate(SynchronizationFixture fixture, string reference) =>
        new(null, 0, fixture.CompareRequest.ExpectedActiveFingerprint, "catalog-sync", reference,
            "Reviewed catalog synchronization.",
            [new("file:" + fixture.RelativePath, "synchronization-catalog", fixture.RelativePath,
                "text/markdown", fixture.Text)]);

    private static string EvidenceReference(InteractionInvocationResult result)
    {
        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        return JsonDocument.Parse(result.DataJson!).RootElement.GetProperty("evidenceReference").GetString()!;
    }

    private sealed record SynchronizationFixture(CatalogSynchronizationService Service,
        CatalogSynchronizationCompareRequest CompareRequest, string Path, string Text, string RelativePath);

    private sealed class SynchronizationRoots(string root) : IAllowedSourceRootResolver
    {
        public bool TryResolve(string allowedRootId, out string canonicalPath)
        {
            canonicalPath = allowedRootId == "synchronization-root" ? root : string.Empty;
            return canonicalPath.Length != 0;
        }
    }
}
