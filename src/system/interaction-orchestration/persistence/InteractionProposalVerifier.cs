using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Sources;

namespace DantesRoleplay.DataAccess.Composition;

internal sealed class InteractionProposalVerifier(
    IApplicationRegistry applications,
    IApplicationActivationReader activations,
    IActiveCatalogFeatureSnapshotProvider snapshots) : IInteractionProposalVerifier
{
    public InteractionResolutionResult Verify(InteractionProposalVerificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Envelope is null || request.Inspected is null || request.Draft is null
            || request.Draft.Steps is null
            || request.Draft.Steps.Count is < 1 or > InteractionContractLimits.ProposalSteps
            || request.Draft.Steps.Any(value => value is null
                || string.IsNullOrWhiteSpace(value.QualifiedId)
                || value.RoleBindings is null || value.DependsOn is null
                || string.IsNullOrWhiteSpace(value.InputJson)))
            return Unsafe("PROPOSAL_DRAFT_INVALID", "The proposed plan is malformed or unbounded.");
        var envelope = request.Envelope;
        var currentApplication = applications.Get(envelope.Host.ApplicationRevision.ApplicationId);
        if (currentApplication is null
            || currentApplication.Revision != envelope.Host.ApplicationRevision.Revision
            || currentApplication.Fingerprint != envelope.Host.ApplicationRevision.Fingerprint
            || !currentApplication.BaseApplications.SequenceEqual(envelope.Host.ApplicationRevision.BaseApplications))
            return Stale("APPLICATION_REVISION_STALE", "The authorized application revision changed during planning.");

        var activation = activations.Current(envelope.Host.ApplicationRevision.ApplicationId);
        if (activation is null || activation.ActivationFingerprint != envelope.Host.EffectiveSetFingerprint)
            return Stale("EFFECTIVE_SET_STALE", "The active application feature set changed during planning.");
        if (!snapshots.TryGetSnapshot(envelope.Host.ApplicationRevision.ApplicationId, out var snapshot))
            return Stale("CATALOG_SNAPSHOT_STALE", "The active application catalog is unavailable.");

        var trustedCurrent = snapshot.Documents
            .Where(value => value.Trust == SourceTrust.Trusted)
            .ToDictionary(value => value.Record.QualifiedId, value => value.Record, StringComparer.Ordinal);
        var inspected = new Dictionary<string, CatalogRecordDefinition>(StringComparer.Ordinal);
        foreach (var item in request.Inspected)
        {
            if (item is null || item.Hit is null || item.Hit.Reference is null || item.ContractJson is null
                || item.Hit.Reference.ApplicationId != envelope.Host.ApplicationRevision.ApplicationId
                || item.Hit.Reference.Lane != InteractionRetrievalLane.TrustedFeature
                || item.Hit.Reference.CatalogFingerprint != snapshot.Manifest.Fingerprint
                || !trustedCurrent.TryGetValue(item.Hit.Reference.QualifiedId, out var record)
                || record.Version != item.Hit.Reference.Version
                || record.ContentFingerprint != item.Hit.Reference.ContentFingerprint
                || record.ContentJson != item.ContractJson)
                return Stale("INSPECTED_CONTRACT_STALE", "An inspected contract is no longer the exact current trusted record.");
            if (!inspected.TryAdd(record.QualifiedId, record))
                return Unsafe("DUPLICATE_INSPECTION", "A contract inspection was duplicated.");
        }

        var steps = new List<InteractionPlanStep>();
        foreach (var draft in request.Draft.Steps)
        {
            if (!inspected.TryGetValue(draft.QualifiedId, out var record))
                return Unsafe("CONTRACT_NOT_INSPECTED", "Every proposed contract must be inspected in the current planning trace.");
            if (record.Version != draft.Version || record.ContentFingerprint != draft.Fingerprint)
                return Stale("PROPOSAL_CONTRACT_STALE", "A proposed contract reference does not match the inspected current record.");
            if (record.Status != "active")
                return Unsupported("CONTRACT_NOT_ACTIVE", "The proposed contract is not active.");
            if (draft.Kind == InteractionPlanStepKind.Query || record.Kind == "procedure")
                return Unsupported("QUERY_CONTRACT_UNSUPPORTED", "No trusted query-contract resolver is enabled in this slice.");
            if (draft.Kind != InteractionPlanStepKind.Action || record.Kind != "mechanic")
                return Unsupported("CONTRACT_KIND_UNSUPPORTED", "The proposed step does not match a supported contract kind.");

            string authoritativeId;
            MechanicRequirements requirements;
            try
            {
                using var document = JsonDocument.Parse(record.ContentJson);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(id.GetString())
                    || !root.TryGetProperty("requirements", out var requirementsElement)
                    || requirementsElement.ValueKind != JsonValueKind.String)
                    return Unsafe("MECHANIC_CONTRACT_INVALID", "The active mechanic contract is malformed.");
                authoritativeId = id.GetString()!;
                requirements = MechanicRequirements.Parse(requirementsElement.GetString()!);
            }
            catch (JsonException)
            {
                return Unsafe("MECHANIC_CONTRACT_INVALID", "The active mechanic requirements are malformed.");
            }

            if (requirements.Roles is null || requirements.Children is null)
                return Unsafe("MECHANIC_REQUIREMENTS_INVALID", "The active mechanic declares invalid generic requirements.");
            if (requirements.Event is not null)
                return Unsupported("EVENT_MECHANIC_NOT_DIRECT", "An event middleware mechanic cannot be proposed as a direct action.");
            var declared = requirements.Roles.Keys.ToHashSet(StringComparer.Ordinal);
            var supplied = draft.RoleBindings.Keys.ToHashSet(StringComparer.Ordinal);
            var unknown = supplied.Except(declared, StringComparer.Ordinal).Order(StringComparer.Ordinal).FirstOrDefault();
            if (unknown is not null)
                return Unsafe("UNKNOWN_MECHANIC_ROLE", "The proposal supplies a role that the mechanic does not declare.");
            var missing = requirements.Roles
                .Where(value => !value.Value.Optional && !supplied.Contains(value.Key))
                .Select(value => value.Key).Order(StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
                return InteractionResolutionResult.NonResolution(
                    InteractionResolutionStatus.NeedsInput,
                    "MISSING_REQUIRED_ROLE",
                    "The selected contract requires additional role bindings.",
                    missing.Select(value => "role:" + value));
            if (requirements.ProjectionProblems().Count > 0 || requirements.CompositionProblems().Count > 0)
                return Unsafe("MECHANIC_REQUIREMENTS_INVALID", "The active mechanic declares invalid generic requirements.");

            InteractionContractReference reference;
            try
            {
                reference = new(
                    InteractionFeatureScope.Application,
                    envelope.Host.ApplicationRevision.ApplicationId,
                    record.QualifiedId,
                    authoritativeId,
                    record.Version,
                    record.ContentFingerprint);
                steps.Add(new(
                    draft.StepId,
                    draft.Kind,
                    reference,
                    draft.DependsOn,
                    draft.RoleBindings,
                    draft.InputJson,
                    envelope.Host.StateRevision));
            }
            catch (InteractionContractException exception)
            {
                return exception.Code.Contains("STALE", StringComparison.Ordinal)
                    ? Stale(exception.Code, "The proposed plan no longer matches current authority.")
                    : Unsafe(exception.Code, "The proposed plan violates the closed interaction contract.");
            }
        }

        try
        {
            return InteractionResolutionResult.Resolved(InteractionProposal.Create(envelope, steps));
        }
        catch (InteractionContractException exception)
        {
            return exception.Code.Contains("STALE", StringComparison.Ordinal)
                ? Stale(exception.Code, "The proposed plan no longer matches current authority.")
                : Unsafe(exception.Code, "The proposed plan violates the closed interaction contract.");
        }
    }

    private static InteractionResolutionResult Stale(string code, string summary) =>
        InteractionResolutionResult.NonResolution(InteractionResolutionStatus.Stale, code, summary, []);

    private static InteractionResolutionResult Unsafe(string code, string summary) =>
        InteractionResolutionResult.NonResolution(InteractionResolutionStatus.Unsafe, code, summary, []);

    private static InteractionResolutionResult Unsupported(string code, string summary) =>
        InteractionResolutionResult.NonResolution(InteractionResolutionStatus.Unsupported, code, summary, []);
}
