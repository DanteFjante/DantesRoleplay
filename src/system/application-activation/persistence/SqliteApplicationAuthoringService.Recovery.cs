using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

public sealed partial class SqliteApplicationAuthoringService
{
    public async Task<InteractionInvocationResult> RecoverAsync(InteractionInvocationHost host,
        int activationRevision, string? expectedActiveFingerprint, CancellationToken cancellationToken = default)
    {
        if (activationRevision < 1 || expectedActiveFingerprint is null || !Hash(expectedActiveFingerprint))
            return Failed("INVALID_PAYLOAD");
        if (host.Budget.DeadlineUtc <= DateTime.UtcNow)
            return Failed("INVOCATION_DEADLINE_EXCEEDED");
        if (host.Budget.RemainingOperations < 1)
            return Failed("INVOCATION_BUDGET_EXHAUSTED");

        var applicationId = host.ApplicationRevision.ApplicationId;
        try
        {
            var selected = activations.ReadRevision(applicationId, activationRevision);
            if (selected is null || selected.PreparationVersion is null)
                return Unavailable();
            if (selected.Winners.Count is 0 or > ApplicationAuthoringLimits.DocumentsPerWrite
                || selected.Winners.Any(value => !value.IsText || value.Length is < 0 or > 64 * 1024)
                || selected.Winners.Sum(value => value.Length) > 64 * 1024)
                return Unavailable();
            var documents = new List<ApplicationCandidateDocumentInput>(selected.Winners.Count);
            foreach (var winner in selected.Winners.OrderBy(value => value.RelativePath, StringComparer.Ordinal))
            {
                var retained = evidence.ReadDocumentEvidence(applicationId, selected.ActivationRevision, winner.LogicalIdentity);
                if (retained is null || retained.IsLegacyMetadataOnly || retained.RetainedBytes is null
                    || retained.ApplicationId != applicationId || retained.ActivationRevision != selected.ActivationRevision
                    || retained.LogicalIdentity != winner.LogicalIdentity || retained.ContentFingerprint != winner.ContentFingerprint
                    || retained.Length != winner.Length || retained.RetainedBytes.LongLength != winner.Length
                    || Convert.ToHexString(SHA256.HashData(retained.RetainedBytes)) != winner.ContentFingerprint)
                    return Unavailable();
                var source = sources.Get(applicationId, winner.SourceId);
                if (source is null || source.ApplicationId != applicationId || source.Trust != winner.Trust
                    || source.Precedence != winner.Precedence)
                    return Unavailable();
                documents.Add(new(winner.LogicalIdentity, winner.SourceId, winner.RelativePath, winner.MediaType,
                    new UTF8Encoding(false, true).GetString(retained.RetainedBytes)));
            }
            var request = new ApplicationCandidateWriteRequest(null, 0, expectedActiveFingerprint, "runtime", null,
                $"Recover retained activation revision {activationRevision}.", documents);
            var operationId = Id(host, applicationId, "operation");
            if (!await db.Operations.AsNoTracking().AnyAsync(value => value.Id == operationId, cancellationToken))
            {
                var current = activations.Current(applicationId);
                if (current is null || current.PreparationVersion is null)
                    return Unavailable();
                if (!string.Equals(current.ActivationFingerprint, expectedActiveFingerprint, StringComparison.Ordinal))
                    return Failed("APPLICATION_CANDIDATE_ACTIVE_STALE");
                var currentByPath = current.Winners.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
                if (currentByPath.Count != selected.Winners.Count || selected.Winners.Any(value =>
                        !currentByPath.TryGetValue(value.RelativePath, out var currentValue)
                        || !SameDocumentIdentity(value, currentValue)))
                    return Unavailable();
            }
            return await WriteCandidateAsync(host, request, cancellationToken);
        }
        catch (DecoderFallbackException) { return Unavailable(); }
        catch (ApplicationActivationException) { return Unavailable(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException
            or IOException or UnauthorizedAccessException) { return Unavailable(); }
    }

    private static bool SameDocumentIdentity(ActivatedApplicationDocument left, ActivatedApplicationDocument right) =>
        left.LogicalIdentity == right.LogicalIdentity && left.SourceId == right.SourceId && left.Trust == right.Trust
        && left.Precedence == right.Precedence && left.RelativePath == right.RelativePath && left.MediaType == right.MediaType
        && left.IsText == right.IsText;

    private static InteractionInvocationResult Unavailable() =>
        InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_RECOVER_UNAVAILABLE",
            "The retained generation cannot be recovered as a bounded candidate.");
}
