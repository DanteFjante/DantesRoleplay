using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Interactions;

/// <summary>An inert existing-document replacement; only the authoring owner may retain/publish it.</summary>
internal sealed record ProcedureIntentAssociationPreparationResult(string Code, ApplicationCandidateDocumentInput? Document);

/// <summary>
/// Exact application procedure phrase edits. The only public-facing input is a typed lookup and
/// exact selection; all source bytes come from the retained candidate owner, never a supplied snapshot.
/// </summary>
internal sealed class ProcedureIntentAssociationPreparation(
    ApplicationCandidateRetainedReader candidates,
    IStandingGrantTargetResolver targets,
    IStandingGrantPolicy grants,
    IApplicationDefinitionChangeReader changes)
{
    internal async Task<ProcedureIntentAssociationPreparationResult> PrepareAsync(InteractionInvocationHost host,
        ApplicationCandidateReference candidate, StandingGrantDefinitionReference selection,
        IReadOnlyList<string> matchPhrases, CancellationToken cancellationToken = default)
    {
        try
        {
            if (candidate.ApplicationId != host.ApplicationRevision.ApplicationId || candidate.Revision < 1
                || selection.Kind != "procedure" || !selection.DefinitionId.StartsWith(candidate.ApplicationId.Value + ".", StringComparison.Ordinal))
                return Unavailable();
            if (!host.Budget.TryConsumeOperation()) return new("INTENT_PREPARATION_BUDGET", null);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var remaining = host.Budget.DeadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) deadline.Cancel();
            else if (remaining.TotalMilliseconds <= int.MaxValue) deadline.CancelAfter(remaining);
            var token = deadline.Token;
            token.ThrowIfCancellationRequested();
            var readback = await candidates.ReadAsync(candidate.ApplicationId, candidate.CandidateId, candidate.Revision, token);
            if (readback is null || readback.RevisionRow.ContentFingerprint != candidate.ContentFingerprint
                || readback.ApplicationRevision.ApplicationId != host.ApplicationRevision.ApplicationId
                || readback.ApplicationRevision.Revision != host.ApplicationRevision.Revision
                || readback.ApplicationRevision.Fingerprint != host.ApplicationRevision.Fingerprint)
                return Unavailable();
            var snapshot = Snapshot(readback);
            var generation = changes.CurrentChange(candidate.ApplicationId);
            if (generation?.Fingerprint != snapshot.ExpectedActiveFingerprint
                || !await AuthorizedAsync(host, snapshot, selection, token)) return Unavailable();
            var documents = readback.Documents;
            var winners = documents.ToDictionary(value => value.Document.RelativePath, value => value.Document, StringComparer.Ordinal);
            var bytes = documents.ToDictionary(value => value.Document.RelativePath, value => value.RetainedBytes, StringComparer.Ordinal);
            ApplicationCandidateDocument? selected = null;
            foreach (var document in documents)
            {
                token.ThrowIfCancellationRequested();
                var path = document.Document.RelativePath;
                if (!path.EndsWith(".md", StringComparison.Ordinal) || !path.Split('/').Contains("procedures", StringComparer.Ordinal)) continue;
                var record = ActivatedApplicationCatalogMaterializer.ParseRetainedRecord(candidate.ApplicationId,
                    document.Document, winners, bytes);
                if (record is null || record.QualifiedId != selection.DefinitionId) continue;
                if (record.Kind != "procedure" || record.Version != selection.Revision || record.ContentFingerprint != selection.ContentFingerprint
                    || document.Document.Trust != SourceTrust.Trusted || document.RetainedBytes.Length > 32_000 || selected is not null)
                    return Unavailable();
                selected = document;
            }
            if (selected is null) return Unavailable();
            var text = new UTF8Encoding(false, true).GetString(selected.RetainedBytes);
            var edited = ProcedureIntentPhraseEditor.Edit(text, matchPhrases);
            if (Encoding.UTF8.GetByteCount(edited) > 32_000) return new("INTENT_PREPARATION_LIMIT", null);
            // A second owner read verifies that the retained lookup is still exact; the policy also
            // rehydrates source/namespace/grant evidence before any replacement bytes leave this helper.
            var current = await candidates.ReadAsync(candidate.ApplicationId, candidate.CandidateId, candidate.Revision, token);
            if (current is null || current.RevisionRow.ContentFingerprint != candidate.ContentFingerprint
                || !await AuthorizedAsync(host, Snapshot(current), selection, token)) return Unavailable();
            var currentGeneration = changes.CurrentChange(candidate.ApplicationId);
            if (generation?.Revision != currentGeneration?.Revision || generation?.Fingerprint != currentGeneration?.Fingerprint)
                return Unavailable();
            token.ThrowIfCancellationRequested();
            var source = selected.Document;
            return new("INTENT_REPLACEMENT_PREPARED", new(source.LogicalIdentity, source.SourceId, source.RelativePath, source.MediaType, edited));
        }
        catch (OperationCanceledException) { return new("INTENT_PREPARATION_CANCELLED", null); }
        catch (InteractionContractException) { return new("INTENT_PREPARATION_INVALID", null); }
        catch { return Unavailable(); }
    }

    private async Task<bool> AuthorizedAsync(InteractionInvocationHost host, ApplicationCandidateSnapshot candidate,
        StandingGrantDefinitionReference selection, CancellationToken cancellationToken)
    {
        var resolved = await targets.ResolveCandidateAsync(host, candidate, selection, cancellationToken);
        var target = resolved.Target;
        if (resolved.Status != StandingGrantTargetResolutionStatus.Available || target is null
            || target.OwnerApplicationId != host.ApplicationRevision.ApplicationId || target.DefinitionId != selection.DefinitionId
            || target.Kind != selection.Kind || target.Revision != selection.Revision || target.ContentFingerprint != selection.ContentFingerprint)
            return false;
        foreach (var capability in new[] { StandingGrantCapability.Read, StandingGrantCapability.Author })
        {
            var decision = await grants.EvaluateAsync(host, new(capability, StandingGrantScope.Application, [target], []), cancellationToken);
            var grant = decision.Grant;
            if (!decision.Allowed || grant is null || grant.GrantReference != host.GrantReference
                || grant.PrincipalReference != host.Principal.PrincipalId || grant.ApplicationId != host.ApplicationRevision.ApplicationId
                || grant.Scope != StandingGrantScope.Application || grant.StateSpaceId is not null
                || !grant.Capabilities.Contains(capability) || grant.Revoked || grant.ExpiresAtUtc <= DateTime.UtcNow) return false;
        }
        return true;
    }

    private static ApplicationCandidateSnapshot Snapshot(ApplicationCandidateRetainedReadback readback)
    {
        var row = readback.RevisionRow;
        return new(new(readback.ApplicationRevision.ApplicationId, row.CandidateId, row.Revision, row.ContentFingerprint),
            row.ApplicationRevision, readback.ApplicationRevision.Fingerprint, row.ExpectedActiveFingerprint, row.Origin,
            row.SynchronizationEvidenceReference, row.NewImplementationReason, row.AuthorGrantReference, row.SourceOperationId,
            readback.Documents, []);
    }
    private static ProcedureIntentAssociationPreparationResult Unavailable() => new("INTENT_PREPARATION_UNAVAILABLE", null);
}
