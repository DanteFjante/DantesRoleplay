using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Interactions.Tests;

public sealed class InteractionExecutionCoordinatorTests
{
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("sample-app");
    private static readonly TrustedPrincipalContext Principal = PrivateOperatorPrincipal.Create("local-loopback", "fixture");

    [Fact]
    public async Task Sequential_execution_records_partial_progress_and_skips_after_failure()
    {
        var registry = new InMemoryApplicationRegistry();
        var revision = registry.Register(new(App, "Sample", "", []));
        var activationFingerprint = Hash("activation");
        var state = new StateSpaceView("state.1", revision, activationFingerprint, 1, DateTime.UtcNow, DateTime.UtcNow);
        var records = new[] { Record("one"), Record("two"), Record("three") };
        var manifest = CatalogNavigationManifest.Create(App, Hash("catalog"), "catalog-lexical-v1",
            [new(App.Value, "Sample", "Fixture catalog.")],
            [new(App.Value, "", "Sample", "Fixture catalog.", CatalogDescriptionStatus.Authored),
             new(App.Value, "mechanics", "Mechanics", "Fixture mechanics.", CatalogDescriptionStatus.Authored)], records);
        var snapshot = new ActiveCatalogFeatureSnapshot(manifest,
            records.Select(record => new ActiveCatalogFeatureDocument(record, SourceTrust.Trusted)).ToArray());
        var activations = new Activation(new(App, 1, revision.Revision, revision.Fingerprint,
            Hash("preview"), Hash("scan"), Hash("candidate"), Hash("dependencies"), activationFingerprint,
            "coverage-v1", true, [], [], "operation.activation", DateTime.UtcNow));
        var spaces = new Spaces(state);
        var verifier = new DantesRoleplay.DataAccess.Composition.InteractionProposalVerifier(
            registry, activations, new Snapshots(snapshot));
        var planRequest = new InteractionAuthorizationRequest(Principal, App, state.StateSpaceId,
            InteractionCapability.Plan, "plan.fixture");
        var host = new InteractionHostContext(Principal, revision, state.StateSpaceId, "session.1",
            InteractionStateRevision.From(state), activationFingerprint, InteractionRoleProfile.Outer,
            new(3, 65_536, 65_536), InteractionAuthorizationDecision.Allow(planRequest, "plan.evidence"));
        var envelope = AuthorizedInteractionEnvelope.Create(InteractionIntent.Parse(JsonSerializer.Serialize(new
        {
            idempotencyKey = "plan.1", intentText = "Run three exact fixture actions.", maximumPlanSteps = 3
        })), host);
        var command = new InteractionPlannerProposalCommand(records.Select((record, index) =>
            new InteractionPlannerDraftStep($"step.{index + 1}", InteractionPlanStepKind.Action,
                record.QualifiedId, 1, record.ContentFingerprint,
                index == 0 ? [] : [$"step.{index}"], new Dictionary<string, string>(), "{}"))
            .ToArray());
        var inspected = records.Select(record =>
        {
            var reference = InteractionFeatureReference.Create(App, InteractionRetrievalLane.TrustedFeature,
                manifest.Fingerprint, record);
            return new InteractionInspectedFeature(InteractionFeatureHit.Create(reference, record, null, null, true),
                record.ContentJson);
        }).ToArray();
        var proposal = verifier.Verify(new(envelope, inspected, command)).Proposal!;
        var authority = new InteractionResolutionExecutionAuthority(
            "interaction-receipt." + new string('a', 32), Principal.PrincipalId, App,
            revision.Revision, revision.Fingerprint, state.StateSpaceId, "session.1",
            InteractionStateRevision.From(state), activationFingerprint, InteractionRoleProfile.Outer.StableKey,
            null, null, "plan.evidence", "plan.1", envelope.Fingerprint, "resolved", proposal.Fingerprint);
        var receiptStore = new Receipts();
        var actions = new Actions();
        var coordinator = new InteractionExecutionCoordinator(new Allow(), new Authority(authority),
            receiptStore, registry, activations, spaces, new Snapshots(snapshot), verifier, actions);

        var outcome = await coordinator.ExecuteAsync(new(authority.ResolutionReceiptId, proposal.Fingerprint,
            "execute.1", command), new(Principal, App, state.StateSpaceId,
            InteractionCapability.Execute, "execute.fixture"));

        Assert.Equal(InteractionExecutionReceiptDisposition.Partial, outcome.Disposition);
        Assert.Equal(2, actions.Calls);
        Assert.Equal([
            InteractionExecutionStepDisposition.Succeeded,
            InteractionExecutionStepDisposition.Failed,
            InteractionExecutionStepDisposition.Skipped
        ], receiptStore.Draft!.Steps.Select(step => step.Disposition));
        Assert.Equal(2, outcome.ActionResults.Count);
    }

    private static CatalogRecordDefinition Record(string id)
    {
        var content = JsonSerializer.Serialize(new
        {
            id = "mechanic." + id,
            requirements = "{\"roles\":{}}",
            source = "return { effects: [] };"
        });
        return new(App.Value, "mechanic", $"{App.Value}.mechanic.{id}", id, id, [], [], "mechanics",
            "active", 1, content, Hash(content), "source", "mechanics/" + id + ".md");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class Allow : IInteractionAuthorizationPolicy
    {
        public InteractionAuthorizationDecision Evaluate(InteractionAuthorizationRequest request) =>
            InteractionAuthorizationDecision.Allow(request, "execute.evidence");
    }
    private sealed class Authority(InteractionResolutionExecutionAuthority value) : IInteractionExecutionAuthorityStore
    {
        public Task<InteractionResolutionExecutionAuthority?> GetAsync(InteractionAuthorizationRequest request,
            string resolutionReceiptId, CancellationToken cancellationToken = default) =>
            Task.FromResult<InteractionResolutionExecutionAuthority?>(resolutionReceiptId == value.ResolutionReceiptId ? value : null);
    }
    private sealed class Activation(ActiveApplicationManifest value) : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) => applicationId == value.ApplicationId ? value : null;
    }
    private sealed class Spaces(StateSpaceView value) : IStateSpaceRegistry
    {
        public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException();
        public StateSpaceView? Get(string stateSpaceId) => stateSpaceId == value.StateSpaceId ? value : null;
        public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier applicationId, string? afterStateSpaceId, int limit) => new([value], null);
    }
    private sealed class Snapshots(ActiveCatalogFeatureSnapshot value) : IActiveCatalogFeatureSnapshotProvider
    {
        public bool TryGetSnapshot(ApplicationIdentifier applicationId, out ActiveCatalogFeatureSnapshot snapshot)
        { snapshot = value; return applicationId == App; }
    }
    private sealed class Actions : IApplicationActionRunner
    {
        public int Calls { get; private set; }
        public Task<ApplicationActionExecutionResult> RunAsync(ApplicationActionExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<ApplicationActionExecutionResult>(Calls == 1
                ? new(ApplicationActionExecutionDisposition.Succeeded, request.ExecutionIdentity.OperationId,
                    request.QualifiedMechanicId, request.ContentFingerprint, request.Seed, "first", 1, [])
                : new(ApplicationActionExecutionDisposition.Failed, request.ExecutionIdentity.OperationId,
                    request.QualifiedMechanicId, request.ContentFingerprint, request.Seed, "", 0,
                    [new("FIXTURE_FAILURE", "The fixture failed.")]));
        }
    }
    private sealed class Receipts : IInteractionReceiptStore
    {
        public InteractionExecutionReceiptDraft? Draft { get; private set; }
        public Task<InteractionReceiptWriteResult> AppendResolutionAsync(InteractionResolutionReceiptDraft draft,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InteractionReceiptWriteResult> AppendExecutionAsync(InteractionExecutionReceiptDraft draft,
            CancellationToken cancellationToken = default)
        {
            Draft = draft;
            var receipt = new InteractionReceiptProjection("interaction-receipt." + new string('b', 32),
                "execution", Principal.PrincipalId, App, "state.1", "execute.1",
                draft.ExecutionRequestFingerprint, "partial", "INTERACTION_EXECUTION_PARTIAL",
                null, draft.SafeSummary, draft.Evidence, DateTime.UtcNow, draft.Consent.ResolutionReceiptId,
                draft.Steps.Select(step => new InteractionExecutionStepReceiptProjection(step.Ordinal,
                    step.ProposalStepId, step.Disposition.ToString().ToLowerInvariant(), step.OperationId)).ToArray());
            return Task.FromResult(InteractionReceiptWriteResult.Appended(receipt));
        }
        public Task<InteractionReceiptProjection?> GetAsync(InteractionAuthorizationRequest authorizationRequest,
            string receiptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
