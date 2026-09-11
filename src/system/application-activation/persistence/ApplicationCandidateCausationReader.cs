using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Reconciles one actual write command. It never substitutes a creation receipt for another causal command.</summary>
internal sealed class ApplicationCandidateCausationReader(DantesRoleplayDbContext db, IApplicationRegistry applications,
    IApplicationActivationReader activations)
{
    internal async Task<ApplicationCandidateCausationEvidence?> ReadAsync(ApplicationCandidateReference candidate,
        string causalCommandId, string operationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (string.IsNullOrWhiteSpace(causalCommandId) || operationId is not { Length: 32 }
            || operationId.Any(value => !(char.IsAsciiDigit(value) || value is >= 'a' and <= 'f'))) return null;
        try
        {
            await using var scope = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
            var reader = new ApplicationCandidateRetainedReader(db, applications);
            var retained = await reader.ReadMetadataAsync(candidate.ApplicationId, candidate.CandidateId, candidate.Revision, cancellationToken);
            if (retained is null || retained.RevisionRow.ContentFingerprint != candidate.ContentFingerprint
                || retained.RevisionRow.SourceOperationId != operationId) return null;
            // Reject oversized operation fields before materializing them; no raw audit projection
            // is returned by this evidence reader. The shared proof enforces the UTF-8/depth bounds.
            var operation = await db.Operations.AsNoTracking().Where(value => value.Id == operationId
                && value.ProjectionJson.Length <= 64 * 1024 && value.GuardEvidenceJson != null
                && value.GuardEvidenceJson.Length <= 64 * 1024).SingleOrDefaultAsync(cancellationToken);
            if (operation is null) return null;
            var basis = await ApplicationCandidateDocumentSelection.ReadBaseAsync(db, activations, candidate.ApplicationId,
                retained.RevisionRow.ExpectedActiveFingerprint, cancellationToken);
            var changed = ApplicationCandidateDocumentSelection.ChangedPaths(retained.Documents, basis);
            if (changed.Count is < 1 or > ApplicationAuthoringLimits.DocumentsPerWrite) return null;
            var documents = await reader.ReadSelectedAsync(retained,
                ApplicationCandidateDocumentSelection.WithKnownSidecars(retained.Documents, changed), cancellationToken);
            var definitions = SqliteApplicationAuthoringService.Definitions(candidate.ApplicationId, documents, changed);
            if (!ApplicationCandidateOperationProof.WriteMatches(operation, retained, definitions, out var command)
                || command is null || command.CommandId != causalCommandId) return null;
            return new(operationId, causalCommandId, candidate, retained.RevisionRow.CanonicalCommandFingerprint,
                retained.RevisionRow.CanonicalCommandFingerprint);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException
            or ApplicationActivationException or ApplicationCatalogMaterializationException)
        { return null; }
    }
}

/// <summary>Host-only immutable correlation. Every consumer must independently recheck current operation authority.</summary>
internal sealed record ApplicationCandidateCausationEvidence(string OperationId, string CausalCommandId,
    ApplicationCandidateReference CandidateRef, string CanonicalCommandFingerprint, string ReceiptFingerprint);
