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
    IApplicationCandidateReviewClosureEvidence? ReviewClosure = null,
    ApplicationCandidateReuseInputV2? ReviewInput = null,
    SystemInnerWorkerResolvedProfile? Profile = null);

/// <summary>Rehydrates actual owner evidence and independently evaluates current application grants.</summary>
internal sealed class SystemTaskApplicationValidationGate(
    DantesRoleplayDbContext db, IApplicationRegistry applications, IApplicationActivationReader activations,
    IStandingGrantTargetResolver targets, IStandingGrantPolicy policy, TimeProvider time,
    ApplicationCandidatePureRuntimeClosureReader? pureClosures = null,
    IInteractionManualContextService? manuals = null,
    IInteractionFeatureRetriever? features = null,
    IEnumerable<IApplicationCandidateReviewClosureReader>? reviewClosures = null)
{
    private readonly IApplicationCandidateReviewClosureReader[] reviewClosures = reviewClosures?
        .OrderBy(value => value.Grammar, StringComparer.Ordinal).ToArray() ?? [];

    internal async Task<SystemTaskValidationAuthority> CheckAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, bool requireValidate,
        string? causationOperationId = null, string? causalCommandId = null,
        CancellationToken cancellationToken = default, bool prepareRuntimeContext = true)
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
        if (manuals is null || features is null || pureClosures is null && reviewClosures.Length == 0)
            return new(null, selection, read.Grant, validateGrant, causation);
        var captured = await CaptureRuntimeReviewAsync(host, candidate, selection,
            read.Grant!, validateGrant!, cancellationToken);
        if (captured.Failure is not null) return captured;
        captured = captured with { Causation = causation };
        return prepareRuntimeContext
            ? await BindRuntimeReviewAsync(host, captured, cancellationToken)
            : captured;
    }

    /// <summary>
    /// A selected-document DTO, narrower sample closure, or Create(material, true) cannot satisfy
    /// this gate. The broader owner proof and its V2/manual/profile binding are still required.
    /// </summary>
    internal static InteractionInvocationResult? ExecutionPrerequisite(SystemTaskValidationAuthority authority)
    {
        if (authority.Failure is not null) return authority.Failure;
        if (authority.ReviewClosure is null)
            return InteractionInvocationResult.Unavailable("INNER_VALIDATION_DEPENDENCIES_UNAVAILABLE",
                "An owner-issued closed review dependency and source proof is required before durable validation admission.");
        if (authority.ReviewInput is null || authority.Profile is null)
            return InteractionInvocationResult.Unavailable("INNER_VALIDATION_CONTEXT_BINDING_UNAVAILABLE",
                "Owner-verified selected-source, manual-context, and reviewer-profile bindings are required.");
        return null;
    }

    private async Task<SystemTaskValidationAuthority> CaptureRuntimeReviewAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, ApplicationCandidateSelectionEvidence selection,
        StandingGrantRevision readGrant, StandingGrantRevision validateGrant, CancellationToken cancellationToken)
    {
        ApplicationCandidatePureRuntimeClosureEvidence? pureClosure = pureClosures is null ? null
            : await pureClosures.ReadAsync(host, candidate, cancellationToken);
        var available = new List<IApplicationCandidateReviewClosureEvidence>();
        if (pureClosure is not null) available.Add(pureClosure);
        var rejected = false;
        foreach (var reader in reviewClosures)
        {
            var result = await reader.ReadAsync(host, candidate, selection, cancellationToken);
            if (result.Status == ApplicationCandidateReviewClosureReadStatus.Rejected) rejected = true;
            else if (result.Status == ApplicationCandidateReviewClosureReadStatus.Available && result.Evidence is not null)
                available.Add(result.Evidence);
            else if (result.Status != ApplicationCandidateReviewClosureReadStatus.NotApplicable)
                rejected = true;
        }
        if (rejected || available.Count != 1 || !await VerifyClosureAsync(
                host, candidate, selection, available.SingleOrDefault(), cancellationToken))
            return new(UnavailableResult("INNER_VALIDATION_DEPENDENCIES_UNAVAILABLE",
                "The candidate is outside one unambiguous owner-issued review grammar."));
        var closure = available[0];
        var retained = await new ApplicationCandidateRetainedReader(db, applications).ReadMetadataAsync(
            candidate.ApplicationId, candidate.CandidateId, candidate.Revision, cancellationToken);
        if (retained is null || retained.RevisionRow.ContentFingerprint != candidate.ContentFingerprint)
            return new(UnavailableResult("INNER_VALIDATION_SELECTION_UNAVAILABLE",
                "The retained candidate changed during review preparation."));
        return new(null, selection, readGrant, validateGrant, null, pureClosure, closure);
    }

    internal async Task<SystemTaskValidationAuthority> BindRuntimeReviewAsync(InteractionInvocationHost host,
        SystemTaskValidationAuthority captured, CancellationToken cancellationToken = default)
    {
        if (captured.Failure is not null || captured.Selection is null
            || captured.ReadGrant is null || captured.ValidateGrant is null
            || captured.ReviewClosure is null)
            return new(UnavailableResult("INNER_VALIDATION_CONTEXT_BINDING_UNAVAILABLE",
                "Review context preparation requires one complete owner capture."));
        var candidate = captured.ReviewClosure.Candidate;
        var selection = captured.Selection;
        var readGrant = captured.ReadGrant;
        var validateGrant = captured.ValidateGrant;
        var closure = captured.ReviewClosure;
        var retained = await new ApplicationCandidateRetainedReader(db, applications).ReadMetadataAsync(
            candidate.ApplicationId, candidate.CandidateId, candidate.Revision, cancellationToken);
        if (retained is null || retained.RevisionRow.ContentFingerprint != candidate.ContentFingerprint)
            return new(UnavailableResult("INNER_VALIDATION_SELECTION_UNAVAILABLE",
                "The retained candidate changed during review preparation."));
        var documents = new List<ApplicationCandidateReviewDocumentV2>();
        foreach (var source in closure.ReviewDocuments)
        {
            if (!TryDocument(source, out var document))
                return new(UnavailableResult("INNER_VALIDATION_SELECTION_UNAVAILABLE",
                    "Trusted retained review documents are unavailable."));
            documents.Add(document!);
        }
        var manualResult = await manuals!.DiscoverAsync(new(host,
            AdvisoryIntent(retained.RevisionRow.NewImplementationReason)), cancellationToken);
        if (manualResult.Tag != InteractionInvocationResultTag.Completed || manualResult.DataJson is null
            || manualResult.CompletionEvidenceReference is null)
            return new(UnavailableResult("INNER_VALIDATION_CONTEXT_BINDING_UNAVAILABLE",
                "A fresh authorized manual context packet is unavailable."));
        using var manualDocument = JsonDocument.Parse(manualResult.DataJson);
        var manualPacket = manualDocument.RootElement.Clone();
        if (!TryManualFingerprint(manualPacket, manualResult.CompletionEvidenceReference, out var manualFingerprint)
            || !AdvisoryBoundsSupported(manualPacket)
            || manualPacket.GetProperty("reusableTasks").GetArrayLength() != 0)
            return new(UnavailableResult("INNER_VALIDATION_CONTEXT_BINDING_UNAVAILABLE",
                "The authorized manual context is incomplete or unsupported."));
        var alternatives = await ReadAlternativesAsync(host, closure, manualPacket, cancellationToken);
        if (alternatives is null)
            return new(UnavailableResult("INNER_VALIDATION_CONTEXT_BINDING_UNAVAILABLE",
                "The authorized alternative selection changed during review preparation."));
        var readAgain = await policy.EvaluateAsync(host,
            new(StandingGrantCapability.Read, StandingGrantScope.Application, selection.Targets, []), cancellationToken);
        if (!Matches(readAgain, host, StandingGrantCapability.Read)
            || !SameGrant(readGrant, readAgain.Grant!))
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
        return captured with { ReviewInput = input, Profile = profile };
    }

    internal async Task<bool> RevalidatePreparedAsync(InteractionInvocationHost host,
        SystemTaskValidationAuthority prepared, string? causationOperationId, string? causalCommandId,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null || prepared.ReviewClosure is null
            || prepared.ReviewInput is null || prepared.Profile is null) return false;
        var current = await CheckAsync(host, prepared.ReviewClosure.Candidate, true,
            causationOperationId, causalCommandId, cancellationToken, prepareRuntimeContext: false);
        if (current.Failure is not null || current.Selection is null || current.ReadGrant is null
            || current.ValidateGrant is null || current.ReviewClosure is null
            || current.Selection.EvidenceFingerprint != prepared.Selection?.EvidenceFingerprint
            || !current.Selection.Targets.SequenceEqual(prepared.Selection.Targets)
            || !SameGrant(current.ReadGrant, prepared.ReadGrant!)
            || !SameGrant(current.ValidateGrant, prepared.ValidateGrant!)
            || current.Causation != prepared.Causation
            || current.ReviewClosure.Candidate != prepared.ReviewClosure.Candidate
            || current.ReviewClosure.Grammar != prepared.ReviewClosure.Grammar
            || current.ReviewClosure.EvidenceFingerprint != prepared.ReviewClosure.EvidenceFingerprint)
            return false;
        foreach (var alternative in prepared.ReviewClosure.ReviewAlternatives)
            if (!await CanReadRetainedAsync(host, alternative, cancellationToken)) return false;
        var closureIds = prepared.ReviewClosure.ReviewAlternatives
            .Select(value => value.Target.DefinitionId).ToHashSet(StringComparer.Ordinal);
        foreach (var alternative in prepared.ReviewInput.Alternatives)
            if (!closureIds.Contains(alternative.DefinitionId)
                && !await CanReadAsync(host, alternative, cancellationToken)) return false;
        return true;
    }

    private async Task<bool> VerifyClosureAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, ApplicationCandidateSelectionEvidence selection,
        IApplicationCandidateReviewClosureEvidence? closure, CancellationToken cancellationToken)
    {
        if (closure is null || closure.Candidate != candidate
            || closure.SelectionEvidenceFingerprint != selection.EvidenceFingerprint
            || string.IsNullOrWhiteSpace(closure.Grammar) || !Hash(closure.EvidenceFingerprint)
            || closure.ReviewDocuments.IsDefaultOrEmpty || closure.Dependencies.IsDefault
            || closure.ReviewAlternatives.IsDefault
            || closure.Dependencies.Distinct().Count() != closure.Dependencies.Length
            || closure.ReviewAlternatives.Select(value => value.Target.DefinitionId)
                .Distinct(StringComparer.Ordinal).Count() != closure.ReviewAlternatives.Length) return false;
        var selectedDocuments = closure.ReviewDocuments.Where(value => value.Role is not ApplicationCandidateReviewDocumentRole.Dependency).ToArray();
        if (selectedDocuments.Length != selection.Documents.Length
            || selection.Documents.Any(selected => selectedDocuments.Count(value => SameDocument(value, selected)) != 1)
            || selectedDocuments.Any(value => !selection.Documents.Any(selected => SameDocument(value, selected)))
            || selection.Targets.Any(target => !selectedDocuments.Any(value => SameDefinition(value.Definition, target)
                && value.Role == ApplicationCandidateReviewDocumentRole.Changed))) return false;
        var dependencyDocuments = closure.ReviewDocuments.Where(value => value.Role == ApplicationCandidateReviewDocumentRole.Dependency).ToArray();
        if (dependencyDocuments.Any(value => !closure.Dependencies.Contains(value.Definition))
            || closure.Dependencies.Any(value => !dependencyDocuments.Any(document => document.Definition == value))) return false;
        foreach (var dependency in closure.Dependencies)
        {
            if (selection.Targets.Any(target => SameDefinition(dependency, target))
                || !await CanReadAsync(host, dependency, cancellationToken)) return false;
        }
        return true;
    }

    private static bool SameDocument(ApplicationCandidateReviewClosureDocument value,
        ApplicationCandidateSelectedDocument selected) => value.Definition == selected.Definition
        && value.Document == selected.Document && value.RetainedBytes.AsSpan().SequenceEqual(selected.RetainedBytes.AsSpan());

    private static bool SameDefinition(StandingGrantDefinitionReference reference,
        StandingGrantDefinitionTarget target) => reference.DefinitionId == target.DefinitionId
        && reference.Kind == target.Kind && reference.Revision == target.Revision
        && reference.ContentFingerprint == target.ContentFingerprint;

    private async Task<IReadOnlyList<ApplicationCandidateReuseAlternative>?> ReadAlternativesAsync(
        InteractionInvocationHost host, IApplicationCandidateReviewClosureEvidence closure,
        JsonElement manualPacket, CancellationToken cancellationToken)
    {
        var alternatives = new List<ApplicationCandidateReuseAlternative>();
        foreach (var alternative in closure.ReviewAlternatives)
        {
            if (!ValidAlternative(host, alternative.Target, alternative.ContractJson)
                || !await CanReadRetainedAsync(host, alternative, cancellationToken)) return null;
            alternatives.Add(new(alternative.Target, alternative.ContractJson));
        }
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
            var existing = alternatives.SingleOrDefault(value => value.Target.DefinitionId == target.DefinitionId);
            if (existing is not null)
            {
                if (existing.Target != target || existing.ContractJson != hit.ContractJson) return null;
                continue;
            }
            alternatives.Add(new(target, hit.ContractJson));
        }
        return alternatives;
    }

    private static bool AdvisoryBoundsSupported(JsonElement packet)
    {
        if (!packet.TryGetProperty("bounded", out var bounded) || bounded.ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || packet.TryGetProperty("boundReasons", out var reasons) && reasons.ValueKind != JsonValueKind.Array)
            return false;
        if (!packet.TryGetProperty("boundReasons", out reasons)) return !bounded.GetBoolean();
        var values = reasons.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String
            ? value.GetString() : null).ToArray();
        if (values.Any(string.IsNullOrWhiteSpace) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            return false;
        var supported = values.All(value => value is "ranked-advisory-sections" or "serialized-manual-sections");
        return supported && bounded.GetBoolean() == (values.Length != 0);
    }

    private static string AdvisoryIntent(string reason) => reason.Length <= 256 ? reason : reason[..256];

    private static bool ValidAlternative(InteractionInvocationHost host,
        StandingGrantDefinitionReference target, string contractJson)
    {
        try
        {
            _ = InteractionCanonicalJson.CanonicalizeObject(contractJson);
            return target.DefinitionId.StartsWith(host.ApplicationRevision.ApplicationId.Value + ".", StringComparison.Ordinal)
                && target.Kind is "query" or "procedure"
                && target.Revision > 0
                && target.ContentFingerprint == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contractJson)));
        }
        catch (JsonException) { return false; }
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

    private async Task<bool> CanReadRetainedAsync(InteractionInvocationHost host,
        ApplicationCandidateReviewAlternativeEvidence alternative, CancellationToken cancellationToken)
    {
        var resolved = await targets.ResolveRetainedAsync(host, alternative.RetainedOrigin,
            alternative.Target, cancellationToken);
        if (resolved.Status != StandingGrantTargetResolutionStatus.Available || resolved.Target is null
            || resolved.Target.DefinitionId != alternative.Target.DefinitionId
            || resolved.Target.Kind != alternative.Target.Kind
            || resolved.Target.Revision != alternative.Target.Revision
            || resolved.Target.ContentFingerprint != alternative.Target.ContentFingerprint
            || resolved.Target.RetainedActivation != alternative.RetainedOrigin) return false;
        return Matches(await policy.EvaluateAsync(host, new(StandingGrantCapability.Read,
            StandingGrantScope.Application, [resolved.Target], []), cancellationToken), host,
            StandingGrantCapability.Read);
    }

    private static bool TryDocument(ApplicationCandidateReviewClosureDocument selected,
        out ApplicationCandidateReviewDocumentV2? document)
    {
        document = null;
        try
        {
            if (!selected.Document.IsText || selected.Document.Trust != SourceTrust.Trusted
                || selected.RetainedBytes.Length != selected.Document.Length) return false;
            var text = new UTF8Encoding(false, true).GetString(selected.RetainedBytes.AsSpan());
            if (Convert.ToHexString(SHA256.HashData(selected.RetainedBytes.AsSpan()))
                != selected.Document.ContentFingerprint) return false;
            document = new(selected.Definition, selected.Role, selected.Document.LogicalIdentity,
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

    private static bool Hash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigitUpper);

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
