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
            if (selected.Winners.Count is 0 || selected.Winners.Count > CatalogNavigation.CatalogNavigationLimits.MaximumRecords * 4
                || selected.Winners.Any(value => value.Length is < 0 or > 10L * 1024 * 1024)
                || selected.Winners.Sum(value => value.Length) > 256L * 1024 * 1024)
                return Unavailable();
            // The request is derived from two immutable generations, so it remains identical on
            // replay after the current pointer changes. Only the changed retained bytes are read.
            var basis = await BaseAsync(applicationId, expectedActiveFingerprint, cancellationToken);
            if (basis is null || basis.PreparationVersion is null) return Unavailable();
            var baseByPath = basis.Winners.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
            if (baseByPath.Count != selected.Winners.Count || selected.Winners.Any(value =>
                    !baseByPath.TryGetValue(value.RelativePath, out var previous) || !SameDocumentIdentity(value, previous)))
                return Unavailable();
            var changed = selected.Winners.Where(value => value != baseByPath[value.RelativePath]).ToArray();
            if (changed.Length is 0 or > ApplicationAuthoringLimits.DocumentsPerWrite
                || changed.Any(value => !value.IsText || value.Length > 64 * 1024)
                || changed.Sum(value => value.Length) > 64 * 1024) return Unavailable();
            var documents = new List<ApplicationCandidateDocumentInput>(changed.Length);
            foreach (var winner in changed.OrderBy(value => value.RelativePath, StringComparer.Ordinal))
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
            return await WriteCandidateAsync(host, request, cancellationToken);
        }
        catch (DecoderFallbackException) { return Unavailable(); }
        catch (ApplicationActivationException exception) when (exception.Code == "APPLICATION_CANDIDATE_BASE_UNAVAILABLE")
        { return Failed("APPLICATION_CANDIDATE_ACTIVE_STALE"); }
        catch (ApplicationActivationException) { return Unavailable(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException
            or IOException or UnauthorizedAccessException) { return Unavailable(); }
    }

    private static bool SameDocumentIdentity(ActivatedApplicationDocument left, ActivatedApplicationDocument right) =>
        left.LogicalIdentity == right.LogicalIdentity && left.SourceId == right.SourceId && left.Trust == right.Trust
        && left.Precedence == right.Precedence && left.RelativePath == right.RelativePath
        && ApplicationCandidateDocumentSelection.SameSourceMediaType(left, right)
        && left.IsText == right.IsText;

    private static InteractionInvocationResult Unavailable() =>
        InteractionInvocationResult.Unavailable("APPLICATION_CANDIDATE_RECOVER_UNAVAILABLE",
            "The retained generation cannot be recovered as a bounded candidate.");
}
