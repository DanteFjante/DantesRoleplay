using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;

namespace DantesRoleplay.Tests;

/// <summary>Scope fixtures only; these tests do not model production authorization.</summary>
public sealed class InteractionCandidateReuseReviewTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("sample-app");
    public void Dispose() => _fixture.Dispose();

    [Theory]
    [InlineData(true, true, true, true, true, "REUSE_EXACT_CONTENT_DUPLICATE", ApplicationCandidateCheckStatus.Invalid)]
    [InlineData(true, true, true, true, false, "REUSE_EXACT_CONTENT_DUPLICATE", ApplicationCandidateCheckStatus.Invalid)]
    [InlineData(false, true, true, true, true, "REUSE_REVIEW_WORKER_UNAVAILABLE", ApplicationCandidateCheckStatus.Unavailable)]
    [InlineData(false, true, true, true, false, "REUSE_REVIEW_WORKER_UNAVAILABLE", ApplicationCandidateCheckStatus.Unavailable)]
    [InlineData(true, false, true, true, true, "REUSE_REVIEW_WORKER_UNAVAILABLE", ApplicationCandidateCheckStatus.Unavailable)]
    [InlineData(true, false, true, true, false, "REUSE_REVIEW_WORKER_UNAVAILABLE", ApplicationCandidateCheckStatus.Unavailable)]
    [InlineData(true, true, false, true, true, "REUSE_CANDIDATE_AUTHORITY", ApplicationCandidateCheckStatus.Unavailable)]
    [InlineData(true, true, false, true, false, "REUSE_CANDIDATE_AUTHORITY", ApplicationCandidateCheckStatus.Unavailable)]
    [InlineData(true, true, true, false, true, "REUSE_CANDIDATE_AUTHORITY", ApplicationCandidateCheckStatus.Unavailable)]
    [InlineData(true, true, true, false, false, "REUSE_CANDIDATE_AUTHORITY", ApplicationCandidateCheckStatus.Unavailable)]
    public async Task Real_discovery_pipeline_rejects_copies_without_fabricating_semantic_review(
        bool includeAlternative, bool readAlternative, bool candidateMapped, bool hasRead, bool appOnly,
        string code, ApplicationCandidateCheckStatus status)
    {
        await using var db = _fixture.CreateContext();
        var candidate = Snapshot(reason: "New");
        var source = candidate.EffectiveDocuments.Single();
        var alternativeBytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(source.RetainedBytes)
            .Replace("id: procedure.new", "id: procedure.existing", StringComparison.Ordinal));
        var alternativeDocument = source.Document with
        {
            RelativePath = "content/procedures/procedure.existing.md", Length = alternativeBytes.Length,
            ContentFingerprint = Convert.ToHexString(SHA256.HashData(alternativeBytes))
        };
        var alternative = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(App, alternativeDocument,
            new Dictionary<string, ActivatedApplicationDocument> { [alternativeDocument.RelativePath] = alternativeDocument },
            new Dictionary<string, byte[]> { [alternativeDocument.RelativePath] = alternativeBytes })!;
        CatalogRecordDefinition[] records = includeAlternative ? [alternative] : [];
        var snapshot = new ActiveCatalogFeatureSnapshot(CatalogNavigationManifest.Create(App, new string('F', 64), "fixture",
            [new(App.Value, "Sample", "Fixture")], [new(App.Value, "", "Sample", "Fixture", CatalogDescriptionStatus.Authored),
                new(App.Value, "procedures", "Procedures", "Fixture procedures", CatalogDescriptionStatus.Authored),
                new(App.Value, alternative.Path, "Procedures", "Fixture procedures", CatalogDescriptionStatus.Authored)], records),
            records.Select(record => new ActiveCatalogFeatureDocument(record, SourceTrust.Trusted)).ToArray())
            { EffectiveSetFingerprint = new string('D', 64) };
        var retriever = new InteractionFeatureRetriever(new Snapshots(snapshot));
        var policy = new FixturePolicy(readAlternative, hasRead);
        var resolver = new FixtureResolver(candidateMapped);
        var manual = new CountingManual(new InteractionManualContextService(new ProcedureStore(db), retriever,
            policy, resolver, new Changes(), []));
        var review = new InteractionCandidateReuseReview(manual, retriever, policy, resolver, new Changes());
        var host = Host(appOnly);
        var result = await review.ReviewAsync(host, candidate);
        Assert.Equal((status, code), Status(result));
        Assert.Null(result.EvidenceReference);
        if (!candidateMapped || !hasRead)
        {
            Assert.Equal(0, manual.Calls);
            Assert.Null(result.ManualPacketResultFingerprint);
        }
        else
        {
            Assert.NotNull(result.ManualPacketResultFingerprint); Assert.NotNull(manual.Last);
            var child = manual.Last.Host;
            Assert.Same(host.Budget, child.Budget);
            Assert.Equal(host.Principal, child.Principal); Assert.Equal(host.ApplicationRevision.ApplicationId, child.ApplicationRevision.ApplicationId);
            Assert.Equal(host.ApplicationRevision.Revision, child.ApplicationRevision.Revision); Assert.Equal(host.ApplicationRevision.Fingerprint, child.ApplicationRevision.Fingerprint);
            Assert.Equal(host.ApplicationRevision.BaseApplications, child.ApplicationRevision.BaseApplications);
            Assert.Equal(host.GrantReference, child.GrantReference); Assert.Equal(host.CommandId, child.CommandId);
            Assert.Equal(host.ParentCommandId, child.ParentCommandId); Assert.Equal(InteractionExecutionProfile.ReadOnly, child.Profile);
            Assert.Equal(host.StateSpaceId, child.StateSpaceId); Assert.Equal(host.StateRevision, child.StateRevision);
        }
        if (!readAlternative) Assert.Empty(result.Alternatives);
        if (status == ApplicationCandidateCheckStatus.Invalid)
            Assert.Equal(alternative.QualifiedId, Assert.Single(result.Alternatives).DefinitionId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reason_hash_and_retained_bounds_fail_closed_without_evidence_reference(bool appOnly)
    {
        var review = new InteractionCandidateReuseReview(null!, null!, null!, null!, new Changes());
        var missingReason = await review.ReviewAsync(Host(appOnly), Snapshot(reason: ""));
        var spoofedHash = await review.ReviewAsync(Host(appOnly), Snapshot(hash: new string('A', 64)));
        var oversized = await review.ReviewAsync(Host(appOnly), Snapshot(bytes: new byte[32_001]));

        Assert.Equal((ApplicationCandidateCheckStatus.Invalid, "REUSE_REASON_REQUIRED"), Status(missingReason));
        Assert.Equal((ApplicationCandidateCheckStatus.Unavailable, "REUSE_RETAINED_EVIDENCE"), Status(spoofedHash));
        Assert.Equal((ApplicationCandidateCheckStatus.Unavailable, "REUSE_CONTEXT_LIMIT"), Status(oversized));
        Assert.Null(missingReason.EvidenceReference); Assert.Null(spoofedHash.EvidenceReference); Assert.Null(oversized.EvidenceReference);
    }

    private static (ApplicationCandidateCheckStatus, string) Status(ApplicationCandidateReuseResult value) =>
        (value.Status, Assert.Single(value.Diagnostics).Code);
    private static ApplicationCandidateSnapshot Snapshot(string reason = "new implementation", string? hash = null, byte[]? bytes = null)
    {
        bytes ??= Encoding.UTF8.GetBytes("---\nid: procedure.new\ncategory: tools\nname: New\nstatus: active\n---\n\n## Description\nNew inspection.\n\n## Instructions\nRead.");
        hash ??= Convert.ToHexString(SHA256.HashData(bytes));
        var document = new ActivatedApplicationDocument("file:procedure.new.md", "source", SourceTrust.Trusted, 0,
            "content/procedures/procedure.new.md", "text/markdown", hash, bytes.Length, true);
        return new(new(App, "candidate-identity-0000000000000000", 1, new string('B', 64)), 1, new string('C', 64),
            new string('D', 64), "runtime", null, reason, "grant.1", "operation.1", [new(document, bytes)], []);
    }
    private static InteractionInvocationHost Host(bool appOnly = false) => appOnly
        ? InteractionInvocationHost.ForApplication(TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"), new(App, 1, new string('C', 64), []), "grant.1", "command.1", InteractionExecutionProfile.Atomic, new(2, DateTime.UtcNow.AddMinutes(1)), "parent.1")
        : new(TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"), new(App, 1, new string('C', 64), []), "state.1", "grant.1", "command.1", "revision.1", InteractionExecutionProfile.Atomic, new(2, DateTime.UtcNow.AddMinutes(1)), "parent.1");
    private sealed class Changes : IApplicationDefinitionChangeReader
    { public ApplicationDefinitionChange? CurrentChange(ApplicationIdentifier id) => new(App, 1, new string('D', 64), "operation.1", DateTime.UnixEpoch, new([], []), new(new string('E', 64), "fixture", true), new("rebuildable", false, false)); public ApplicationDefinitionChange? RevisionChange(ApplicationIdentifier id, int revision) => CurrentChange(id); public IReadOnlyList<ApplicationDefinitionChange> ChangesAfter(ApplicationIdentifier id, int after, int limit) => []; }
    private sealed class Snapshots(ActiveCatalogFeatureSnapshot snapshot) : IActiveCatalogFeatureSnapshotProvider
    { public bool TryGetSnapshot(ApplicationIdentifier applicationId, out ActiveCatalogFeatureSnapshot value) { value = snapshot; return true; } }
    private sealed class CountingManual(IInteractionManualContextService actual) : IInteractionManualContextService
    {
        internal int Calls;
        internal InteractionManualContextRequest? Last;
        public Task<InteractionInvocationResult> DiscoverAsync(InteractionManualContextRequest request, CancellationToken cancellationToken = default)
        { Calls++; Last = request; return actual.DiscoverAsync(request, cancellationToken); }
    }
    // Test-only permission fixtures. Passing this pipeline does not establish production issuance or resolution.
    private sealed class FixturePolicy(bool readAlternative, bool hasRead) : IStandingGrantPolicy
    {
        public Task<StandingGrantDecision> EvaluateAsync(InteractionInvocationHost host, StandingGrantRequirement requirement, CancellationToken cancellationToken = default)
        {
            var allowed = hasRead && (readAlternative || requirement.Definitions.All(value => !value.DefinitionId.EndsWith(".existing", StringComparison.Ordinal)));
            var grant = new StandingGrantRevision(host.GrantReference, "fixture.grant", 1, new string('A', 64), host.Principal.PrincipalId,
                App, StandingGrantScope.Application, null, [StandingGrantCapability.Read],
                new(StandingGrantDefinitionMode.ExactIds, requirement.Definitions.Select(value => value.DefinitionId).ToArray(), []),
                [], 16, DateTime.UtcNow.AddHours(1), false, "fixture.issuance");
            return Task.FromResult(new StandingGrantDecision(allowed, "FIXTURE", allowed ? grant : null,
                new(host.Principal.PrincipalId, "fixture", "read", "application", host.CommandId, allowed, "fixture")));
        }
    }
    private sealed class FixtureResolver(bool candidateMapped) : IStandingGrantTargetResolver
    {
        public Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host, StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Available, "FIXTURE",
                new(selection.DefinitionId, selection.Kind, App, "sample-app.procedure", "fixture.owner", selection.Revision, selection.ContentFingerprint)));
        public Task<StandingGrantTargetResolution> ResolveCandidateAsync(InteractionInvocationHost host, ApplicationCandidateSnapshot candidate,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default) => candidateMapped
            ? ResolveAsync(host, selection, cancellationToken)
            : Task.FromResult(new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Unavailable, "FIXTURE", null));
    }
}
