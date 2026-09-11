using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;
using DantesRoleplay.SystemCapabilities;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.SystemTasks.Persistence;

internal sealed record SystemTaskValidationAuthority(
    InteractionInvocationResult? Failure,
    ApplicationCandidateSelectionEvidence? Selection = null,
    StandingGrantRevision? ReadGrant = null,
    StandingGrantRevision? ValidateGrant = null,
    ApplicationCandidateCausationEvidence? Causation = null,
    ApplicationCandidatePureRuntimeClosureEvidence? PureClosure = null,
    ApplicationCandidateReuseInputV2? ReviewInput = null,
    SystemInnerWorkerResolvedProfile? Profile = null);

/// <summary>Rehydrates actual owner evidence and independently evaluates current application grants.</summary>
internal sealed class SystemTaskApplicationValidationGate(
    DantesRoleplayDbContext db, IApplicationRegistry applications, IApplicationActivationReader activations,
    IStandingGrantTargetResolver targets, IStandingGrantPolicy policy, TimeProvider time,
    ApplicationCandidatePureRuntimeClosureReader? pureClosures = null,
    IInteractionManualContextService? manuals = null,
    IInteractionFeatureRetriever? features = null)
{
    internal async Task<SystemTaskValidationAuthority> CheckAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, bool requireValidate,
        string? causationOperationId = null, string? causalCommandId = null,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Validation authority must join the owner's transaction.");
        if (host.StateSpaceId is not null || host.StateRevision is not null
            || host.Profile != InteractionExecutionProfile.ReadOnly
            || candidate.ApplicationId != host.ApplicationRevision.ApplicationId || candidate.ApplicationId.IsSystem)
            return Failed("INNER_VALIDATION_SCOPE_INVALID", "Validation requires its exact application scope.");
        if (host.Budget.DeadlineUtc <= time.GetUtcNow().UtcDateTime)
            return Unavailable("INNER_VALIDATION_DEADLINE_EXPIRED", "The validation deadline has elapsed.");
        var current = applications.Get(candidate.ApplicationId);
        if (current is null || current.Revision != host.ApplicationRevision.Revision
            || current.Fingerprint != host.ApplicationRevision.Fingerprint
            || !current.BaseApplications.SequenceEqual(host.ApplicationRevision.BaseApplications))
            return Failed("INNER_VALIDATION_APPLICATION_STALE", "The application revision is no longer current.");
        var selection = await new ApplicationCandidateSelectionReader(db, applications, activations, targets)
            .ReadAsync(host, candidate, cancellationToken);
        if (selection is null || selection.Candidate != candidate || selection.Targets.IsDefaultOrEmpty
            || selection.Targets.Any(target => target.Candidate != candidate
                || target.OwnerApplicationId != candidate.ApplicationId || target.RetainedActivation is not null))
            return Unavailable("INNER_VALIDATION_SELECTION_UNAVAILABLE", "Exact candidate selection evidence is unavailable.");

        var read = await policy.EvaluateAsync(host,
            new(StandingGrantCapability.Read, StandingGrantScope.Application, selection.Targets, []), cancellationToken);
        if (!Matches(read, host, StandingGrantCapability.Read)) return Denied(read);
        StandingGrantRevision? validateGrant = null;
        if (requireValidate)
        {
            var validate = await policy.EvaluateAsync(host,
                new(StandingGrantCapability.Validate, StandingGrantScope.Application, selection.Targets, []), cancellationToken);
            if (!Matches(validate, host, StandingGrantCapability.Validate)) return Denied(validate);
            validateGrant = validate.Grant;
            if (validateGrant!.GrantReference != read.Grant!.GrantReference
                || validateGrant.Revision != read.Grant.Revision
                || validateGrant.ContentFingerprint != read.Grant.ContentFingerprint)
                return Unavailable("INNER_VALIDATION_AUTHORITY_CHANGED", "Validation authority changed during the check.");
        }

        ApplicationCandidateCausationEvidence? causation = null;
        if ((causationOperationId is null) != (causalCommandId is null))
            return Failed("INNER_VALIDATION_CAUSATION_INVALID", "Causation requires both its operation and causal command.");
        if (causationOperationId is not null)
        {
            causation = await new ApplicationCandidateCausationReader(db, applications, activations)
                .ReadAsync(candidate, causalCommandId!, causationOperationId, cancellationToken);
            if (causation is null || causation.CandidateRef != candidate
                || causation.OperationId != causationOperationId || causation.CausalCommandId != causalCommandId)
                return Unavailable("INNER_VALIDATION_CAUSATION_UNAVAILABLE", "The exact causal authoring receipt could not be verified.");
        }
        if (!requireValidate) return new(null, selection, read.Grant, validateGrant, causation);
        if (pureClosures is null || manuals is null || features is null)
            return new(null, selection, read.Grant, validateGrant, causation);
        var prepared = await PreparePureRuntimeAsync(host, candidate, selection, read.Grant!, validateGrant!, cancellationToken);
        return prepared.Failure is null
            ? prepared with { Causation = causation }
            : prepared;
    }

    /// <summary>
    /// A selected-document DTO, narrower sample closure, or Create(material, true) cannot satisfy
    /// this gate. The broader owner proof and its V2/manual/profile binding are still required.
    /// </summary>
    internal static InteractionInvocationResult? ExecutionPrerequisite(SystemTaskValidationAuthority authority)
    {
        if (authority.Failure is not null) return authority.Failure;
        if (authority.PureClosure is null)
            return InteractionInvocationResult.Unavailable("INNER_VALIDATION_DEPENDENCIES_UNAVAILABLE",
                "An owner-issued closed pure-runtime dependency and sidecar proof is required before durable validation admission.");
        if (authority.ReviewInput is null || authority.Profile is null)
            return InteractionInvocationResult.Unavailable("INNER_VALIDATION_CONTEXT_BINDING_UNAVAILABLE",
                "Owner-verified selected-source, manual-context, and reviewer-profile bindings are required.");
        return null;
    }

    private async Task<SystemTaskValidationAuthority> PreparePureRuntimeAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, ApplicationCandidateSelectionEvidence selection,
        StandingGrantRevision readGrant, StandingGrantRevision validateGrant, CancellationToken cancellationToken)
    {
        var closure = await pureClosures!.ReadAsync(host, candidate, cancellationToken);
        if (closure is null || closure.Candidate != candidate
            || closure.SelectionEvidenceFingerprint != selection.EvidenceFingerprint
            || closure.Definitions.IsDefaultOrEmpty || closure.Definitions.Length != selection.Targets.Length
            || closure.Definitions.Any(value => !selection.Targets.Any(target =>
                target.DefinitionId == value.Plan.Definition.DefinitionId
                && target.Kind == value.Plan.Definition.Kind
                && target.Revision == value.Plan.Definition.Revision
                && target.ContentFingerprint == value.Plan.Definition.ContentFingerprint)))
            return new(UnavailableResult("INNER_VALIDATION_DEPENDENCIES_UNAVAILABLE",
                "The candidate is outside the closed pure-runtime review grammar."));
        var retained = await new ApplicationCandidateRetainedReader(db, applications).ReadMetadataAsync(
            candidate.ApplicationId, candidate.CandidateId, candidate.Revision, cancellationToken);
        if (retained is null || retained.RevisionRow.ContentFingerprint != candidate.ContentFingerprint)
            return new(UnavailableResult("INNER_VALIDATION_SELECTION_UNAVAILABLE",
                "The retained candidate changed during review preparation."));
        var documents = new List<ApplicationCandidateReviewDocumentV2>();
        foreach (var definition in closure.Definitions)
        {
            if (!TryDocument(definition.Markdown, ApplicationCandidateReviewDocumentRole.Changed, out var markdown)
                || !TryDocument(definition.JavaScript, ApplicationCandidateReviewDocumentRole.Sidecar, out var javascript))
                return new(UnavailableResult("INNER_VALIDATION_SELECTION_UNAVAILABLE",
                    "Trusted retained review documents are unavailable."));
            documents.Add(markdown!);
            documents.Add(javascript!);
        }
        var manualResult = await manuals!.DiscoverAsync(new(host, retained.RevisionRow.NewImplementationReason), cancellationToken);
        if (manualResult.Tag != InteractionInvocationResultTag.Completed || manualResult.DataJson is null
            || manualResult.CompletionEvidenceReference is null)
            return new(UnavailableResult("INNER_VALIDATION_CONTEXT_BINDING_UNAVAILABLE",
                "A fresh authorized manual context packet is unavailable."));
        using var manualDocument = JsonDocument.Parse(manualResult.DataJson);
        var manualPacket = manualDocument.RootElement.Clone();
        if (!TryManualFingerprint(manualPacket, manualResult.CompletionEvidenceReference, out var manualFingerprint)
            || manualPacket.GetProperty("bounded").GetBoolean()
            || manualPacket.GetProperty("reusableTasks").GetArrayLength() != 0)
            return new(UnavailableResult("INNER_VALIDATION_CONTEXT_BINDING_UNAVAILABLE",
                "The authorized manual context is incomplete or unsupported."));
        var alternatives = await ReadAlternativesAsync(host, manualPacket, cancellationToken);
        if (alternatives is null)
            return new(UnavailableResult("INNER_VALIDATION_CONTEXT_BINDING_UNAVAILABLE",
                "The authorized alternative selection changed during review preparation."));
        var readAgain = await policy.EvaluateAsync(host,
            new(StandingGrantCapability.Read, StandingGrantScope.Application, selection.Targets, []), cancellationToken);
        var validateAgain = await policy.EvaluateAsync(host,
            new(StandingGrantCapability.Validate, StandingGrantScope.Application, selection.Targets, []), cancellationToken);
        if (!Matches(readAgain, host, StandingGrantCapability.Read)
            || !Matches(validateAgain, host, StandingGrantCapability.Validate)
            || !SameGrant(readGrant, readAgain.Grant!) || !SameGrant(validateGrant, validateAgain.Grant!))
            return new(UnavailableResult("INNER_VALIDATION_AUTHORITY_CHANGED",
                "Validation authority changed during review preparation."));
        var input = ApplicationCandidateReuseInputV2.Create(new(candidate.ApplicationId.Value,
            retained.RevisionRow.NewImplementationReason, documents, manualPacket, alternatives), closureComplete: true);
        var schema = ApplicationCandidateReuseJudgmentOutputV2.OutputSchema(input);
        var schemaFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema)));
        var profile = new SystemInnerWorkerResolvedProfile(new(new SystemInnerWorkerSubject.ApplicationCandidateValidation(candidate),
            host, input.ModelInputJson, schema), SystemInnerWorkerCandidateReviewer.ProfileVersion,
            SystemInnerWorkerCandidateReviewer.Profile, schemaFingerprint, [], ContextReferences(manualPacket),
            new(manualResult.CompletionEvidenceReference, manualFingerprint), Provenance("validate", validateGrant),
            new SystemInnerWorkerAiBudget(toolCalls: 0), Provenance("read", readGrant));
        return new(null, selection, readGrant, validateGrant, null, closure, input, profile);
    }

    private async Task<IReadOnlyList<ApplicationCandidateReuseAlternative>?> ReadAlternativesAsync(
        InteractionInvocationHost host, JsonElement manualPacket, CancellationToken cancellationToken)
    {
        var alternatives = new List<ApplicationCandidateReuseAlternative>();
        foreach (var candidate in manualPacket.GetProperty("candidates").EnumerateArray())
        {
            var reference = candidate.GetProperty("reference");
            if (reference.GetProperty("applicationId").GetString() != host.ApplicationRevision.ApplicationId.Value
                || reference.GetProperty("lane").GetString() != "trustedFeature") return null;
            var target = new StandingGrantDefinitionReference(reference.GetProperty("qualifiedId").GetString()!,
                reference.GetProperty("kind").GetString()!, reference.GetProperty("version").GetInt32(),
                reference.GetProperty("contentFingerprint").GetString()!);
            var found = await features!.SearchAsync(new(host.ApplicationRevision.ApplicationId,
                InteractionRetrievalLane.TrustedFeature), new(target.DefinitionId, 1, [target.Kind]), cancellationToken);
            var hit = found.Hits.SingleOrDefault(value => value.Reference.QualifiedId == target.DefinitionId
                && value.Reference.Kind == target.Kind && value.Reference.Version == target.Revision
                && value.Reference.ContentFingerprint == target.ContentFingerprint);
            if (hit is null || !await CanReadAsync(host, target, cancellationToken)) return null;
            alternatives.Add(new(target, hit.ContractJson));
        }
        return alternatives;
    }

    private async Task<bool> CanReadAsync(InteractionInvocationHost host,
        StandingGrantDefinitionReference reference, CancellationToken cancellationToken)
    {
        var resolved = await targets.ResolveAsync(host, reference, cancellationToken);
        if (resolved.Status != StandingGrantTargetResolutionStatus.Available || resolved.Target is null
            || resolved.Target.DefinitionId != reference.DefinitionId || resolved.Target.Kind != reference.Kind
            || resolved.Target.Revision != reference.Revision
            || resolved.Target.ContentFingerprint != reference.ContentFingerprint) return false;
        return Matches(await policy.EvaluateAsync(host, new(StandingGrantCapability.Read,
            StandingGrantScope.Application, [resolved.Target], []), cancellationToken), host, StandingGrantCapability.Read);
    }

    private static bool TryDocument(ApplicationCandidateSelectedDocument selected,
        ApplicationCandidateReviewDocumentRole role, out ApplicationCandidateReviewDocumentV2? document)
    {
        document = null;
        try
        {
            if (!selected.Document.IsText || selected.Document.Trust != SourceTrust.Trusted
                || selected.RetainedBytes.Length != selected.Document.Length) return false;
            var text = new UTF8Encoding(false, true).GetString(selected.RetainedBytes.AsSpan());
            if (Convert.ToHexString(SHA256.HashData(selected.RetainedBytes.AsSpan()))
                != selected.Document.ContentFingerprint) return false;
            document = new(selected.Definition, role, selected.Document.LogicalIdentity,
                selected.Document.RelativePath, selected.Document.MediaType, selected.Document.ContentFingerprint, text);
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

    private static bool TryManualFingerprint(JsonElement packet, string evidenceReference, out string fingerprint)
    {
        fingerprint = string.Empty;
        try
        {
            fingerprint = packet.GetProperty("resultFingerprint").GetString()!;
            var mutable = JsonNode.Parse(packet.GetRawText())!.AsObject();
            mutable["resultFingerprint"] = new string('0', 64);
            return fingerprint is { Length: 64 } && fingerprint.All(char.IsAsciiHexDigitUpper)
                && Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    InteractionCanonicalJson.CanonicalizeObject(mutable.ToJsonString())))) == fingerprint
                && evidenceReference == "manual.context." + fingerprint.ToLowerInvariant();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return false; }
    }

    private static IReadOnlyList<string> ContextReferences(JsonElement packet) => packet.GetProperty("manualSections")
        .EnumerateArray().Select(value => value.GetProperty("section").GetProperty("reference").GetString()!)
        .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static SystemInnerWorkerAuthorityProvenance Provenance(string capability, StandingGrantRevision grant) =>
        new("standing-grant." + capability + "." + grant.GrantReference,
            grant.Revision.ToString(CultureInfo.InvariantCulture), grant.ContentFingerprint);

    private static bool SameGrant(StandingGrantRevision left, StandingGrantRevision right) =>
        left.GrantReference == right.GrantReference && left.Revision == right.Revision
        && left.ContentFingerprint == right.ContentFingerprint;

    private static InteractionInvocationResult UnavailableResult(string code, string message) =>
        InteractionInvocationResult.Unavailable(code, message);

    private bool Matches(StandingGrantDecision decision, InteractionInvocationHost host, StandingGrantCapability capability) =>
        decision.Allowed && decision.Grant is { } grant && !grant.Revoked
        && grant.GrantReference == host.GrantReference && grant.PrincipalReference == host.Principal.PrincipalId
        && grant.ApplicationId == host.ApplicationRevision.ApplicationId && grant.Scope == StandingGrantScope.Application
        && grant.StateSpaceId is null && grant.Capabilities.Contains(capability)
        && grant.ExpiresAtUtc > time.GetUtcNow().UtcDateTime;

    private static SystemTaskValidationAuthority Denied(StandingGrantDecision decision) =>
        decision.Code.Contains("UNAVAILABLE", StringComparison.Ordinal)
            ? Unavailable("INNER_VALIDATION_AUTHORITY_UNAVAILABLE", "Current application authority is unavailable.")
            : Failed("INNER_VALIDATION_NOT_AUTHORIZED", "Current application authority does not permit this validation operation.");
    private static SystemTaskValidationAuthority Failed(string code, string message) => new(InteractionInvocationResult.Failed(code, message));
    private static SystemTaskValidationAuthority Unavailable(string code, string message) => new(InteractionInvocationResult.Unavailable(code, message));
}
