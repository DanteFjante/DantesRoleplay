using System.Security.Cryptography;
using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Interactions;

internal sealed record IntentMatchAssociationWriteRequest(
    string? CandidateId, int ExpectedCandidateRevision,
    StandingGrantDefinitionReference Target, IReadOnlyList<string> MatchPhrases);

/// <summary>
/// Stages a versioned Matches-only candidate against one exact current target. The application
/// authoring owner retains, validates and publishes the candidate; this service owns no registry.
/// </summary>
internal sealed class IntentMatchAssociationService(
    IApplicationActivationReader activations, IActivatedApplicationEvidenceReader evidence,
    IStandingGrantTargetResolver targets, IStandingGrantPolicy grants,
    IApplicationAuthoringService authoring)
{
    internal async Task<InteractionInvocationResult> WriteAsync(InteractionInvocationHost host,
        IntentMatchAssociationWriteRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.Target is null || request.MatchPhrases is null
            || request.ExpectedCandidateRevision is < 0 or int.MaxValue
            || request.Target.Kind is not ("procedure" or "mechanic")
            || request.ExpectedCandidateRevision > 0 && request.CandidateId is null)
            return Failed("INTENT_ASSOCIATION_INVALID");
        if (!host.Budget.TryConsumeOperation()) return Failed("INVOCATION_BUDGET_EXHAUSTED");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = host.Budget.DeadlineUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) deadline.Cancel();
        else if (remaining.TotalMilliseconds <= int.MaxValue) deadline.CancelAfter(remaining);
        var token = deadline.Token;
        try
        {
            var initial = await ResolveAuthorizedAsync(host, request.Target, token);
            if (initial is null) return Unavailable("INTENT_ASSOCIATION_AUTHORITY_UNAVAILABLE");
            var current = activations.Current(host.ApplicationRevision.ApplicationId);
            if (current is null || initial.Origin is null
                || !Matches(current, initial.Origin))
                return Unavailable("INTENT_ASSOCIATION_TARGET_STALE");
            var selected = Find(current, request.Target, token);
            if (selected is null) return Unavailable("INTENT_ASSOCIATION_TARGET_UNAVAILABLE");
            var edited = request.Target.Kind == "procedure"
                ? ProcedureIntentPhraseEditor.Edit(selected.Markdown, request.MatchPhrases)
                : ProcedureIntentPhraseEditor.EditMechanic(selected.Markdown,
                    selected.Sidecar ?? throw new InvalidOperationException("Mechanic sidecar missing."), request.MatchPhrases);
            if (Encoding.UTF8.GetByteCount(edited) > 32_000)
                return Failed("INTENT_ASSOCIATION_LIMIT");

            var refreshed = await ResolveAuthorizedAsync(host, request.Target, token);
            var after = activations.Current(host.ApplicationRevision.ApplicationId);
            if (refreshed is null || after is null || refreshed.Origin is null
                || !Matches(after, refreshed.Origin)
                || after.ActivationFingerprint != current.ActivationFingerprint)
                return Failed("INTENT_ASSOCIATION_TARGET_STALE");

            var source = selected.Document;
            return await authoring.WriteCandidateAsync(host, new(
                request.CandidateId, request.ExpectedCandidateRevision, current.ActivationFingerprint,
                "runtime", null, $"Update alternate intent phrases for existing {request.Target.Kind} {request.Target.DefinitionId}.",
                [new(source.LogicalIdentity, source.SourceId, source.RelativePath, source.MediaType, edited)]), token);
        }
        catch (OperationCanceledException)
        {
            return InteractionInvocationResult.Cancelled("INTENT_ASSOCIATION_CANCELLED",
                "Intent association authoring was cancelled; reconcile the command before retrying.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or DecoderFallbackException or ApplicationActivationException
            or ApplicationCatalogMaterializationException)
        {
            return Unavailable("INTENT_ASSOCIATION_SOURCE_UNAVAILABLE");
        }
    }

    private async Task<ResolvedTarget?> ResolveAuthorizedAsync(
        InteractionInvocationHost host, StandingGrantDefinitionReference selection,
        CancellationToken cancellationToken)
    {
        var resolved = await targets.ResolveAsync(host, selection, cancellationToken);
        var target = resolved.Target;
        if (resolved.Status != StandingGrantTargetResolutionStatus.Available || target is null
            || target.DefinitionId != selection.DefinitionId || target.Kind != selection.Kind
            || target.Revision != selection.Revision || target.ContentFingerprint != selection.ContentFingerprint
            || target.OwnerApplicationId != host.ApplicationRevision.ApplicationId)
            return null;
        // Read authority permits retained source preparation. Author authority is deliberately
        // checked later by the application writer inside its SQLite mutation transaction.
        var decision = await grants.EvaluateAsync(host,
            new(StandingGrantCapability.Read, StandingGrantScope.Application, [target], []), cancellationToken);
        if (!decision.Allowed || decision.Grant is not { } grant
            || grant.GrantReference != host.GrantReference
            || grant.PrincipalReference != host.Principal.PrincipalId
            || grant.ApplicationId != host.ApplicationRevision.ApplicationId
            || grant.Scope != StandingGrantScope.Application || grant.StateSpaceId is not null
            || grant.Revoked || grant.ExpiresAtUtc <= DateTime.UtcNow
            || !grant.Capabilities.Contains(StandingGrantCapability.Read)) return null;
        return new(target, resolved.CurrentActivation);
    }

    private SelectedDocument? Find(ActiveApplicationManifest activation,
        StandingGrantDefinitionReference selection, CancellationToken cancellationToken)
    {
        if (activation.Winners.Count > CatalogNavigationLimits.MaximumRecords * 4) return null;
        SelectedDocument? found = null;
        var candidates = activation.Winners.Where(value => value.Trust == SourceTrust.Trusted
            && value.IsText && value.Length is >= 0 and <= 32_000
            && ActivatedApplicationCatalogMaterializer.TryRecordKind(value.RelativePath, out var kind)
            && kind == selection.Kind).ToArray();
        foreach (var document in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var retained = Read(activation, document);
            if (retained is null) return null;
            var winners = new Dictionary<string, ActivatedApplicationDocument>(StringComparer.Ordinal)
                { [document.RelativePath] = document };
            var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal)
                { [document.RelativePath] = retained };
            string? sidecarText = null;
            if (selection.Kind == "mechanic")
            {
                var sidecarPath = Path.ChangeExtension(document.RelativePath, ".js").Replace('\\', '/');
                var sidecar = activation.Winners.SingleOrDefault(value => value.RelativePath == sidecarPath);
                if (sidecar is null || !sidecar.IsText || sidecar.Trust != document.Trust
                    || sidecar.SourceId != document.SourceId) return null;
                var sidecarBytes = Read(activation, sidecar);
                if (sidecarBytes is null) return null;
                winners.Add(sidecarPath, sidecar);
                bytes.Add(sidecarPath, sidecarBytes);
                sidecarText = Strict(sidecarBytes);
            }
            var record = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(
                activation.ApplicationId, document, winners, bytes);
            if (record is null || record.QualifiedId != selection.DefinitionId) continue;
            if (record.Kind != selection.Kind || record.Version != selection.Revision
                || record.ContentFingerprint != selection.ContentFingerprint || found is not null)
                return null;
            found = new(document, Strict(retained), sidecarText);
        }
        return found;
    }

    private byte[]? Read(ActiveApplicationManifest activation, ActivatedApplicationDocument document)
    {
        var retained = evidence.ReadDocumentEvidence(activation.ApplicationId,
            activation.ActivationRevision, document.LogicalIdentity);
        if (retained is null || retained.IsLegacyMetadataOnly || retained.RetainedBytes is not { } bytes
            || retained.ApplicationId != activation.ApplicationId
            || retained.ActivationRevision != activation.ActivationRevision
            || retained.LogicalIdentity != document.LogicalIdentity
            || retained.ContentFingerprint != document.ContentFingerprint
            || retained.Length != document.Length || bytes.LongLength != document.Length
            || Convert.ToHexString(SHA256.HashData(bytes)) != document.ContentFingerprint)
            return null;
        return bytes;
    }

    private static bool Matches(ActiveApplicationManifest manifest, StandingGrantActivationOrigin origin) =>
        manifest.ActivationRevision == origin.ActivationRevision
        && manifest.ActivationFingerprint == origin.ActivationFingerprint
        && manifest.ApplicationRevision == origin.ApplicationRevision
        && manifest.ApplicationFingerprint == origin.ApplicationFingerprint;
    private static string Strict(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes);
    private static InteractionInvocationResult Failed(string code) =>
        InteractionInvocationResult.Failed(code, "The intent association request was rejected.");
    private static InteractionInvocationResult Unavailable(string code) =>
        InteractionInvocationResult.Unavailable(code,
            "The exact current target could not be prepared for intent association authoring.");
    private sealed record SelectedDocument(ActivatedApplicationDocument Document,
        string Markdown, string? Sidecar);
    private sealed record ResolvedTarget(StandingGrantDefinitionTarget Target,
        StandingGrantActivationOrigin? Origin);
}
