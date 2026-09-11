using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.ApplicationActivation;

/// <summary>Derives bounded authoring context from exact candidate/base metadata; never implies complete dependency coverage.</summary>
internal static class ApplicationCandidateDocumentSelection
{
    internal static async Task<ActiveApplicationManifest?> ReadBaseAsync(DantesRoleplayDbContext db,
        IApplicationActivationReader activations, ApplicationIdentifier applicationId, string? fingerprint, CancellationToken cancellationToken)
    {
        if (fingerprint is null) return null;
        var revision = await db.Set<ApplicationActivationRevisionRecord>().AsNoTracking()
            .Where(value => value.ApplicationId == applicationId.Value && value.ActivationFingerprint == fingerprint)
            .Select(value => (int?)value.ActivationRevision).SingleOrDefaultAsync(cancellationToken);
        var manifest = revision is null ? null : activations.ReadRevision(applicationId, revision.Value);
        if (manifest is null || manifest.ActivationFingerprint != fingerprint || manifest.ApplicationId != applicationId)
            throw new ApplicationActivationException("APPLICATION_CANDIDATE_BASE_UNAVAILABLE", "The pinned activation generation is unavailable.");
        return manifest;
    }

    internal static IReadOnlyList<string> ChangedPaths(IReadOnlyList<ActivatedApplicationDocument> documents,
        ActiveApplicationManifest? basis)
    {
        var prior = basis?.Winners.ToDictionary(value => value.RelativePath, StringComparer.Ordinal) ?? [];
        return documents.Where(value => !prior.TryGetValue(value.RelativePath, out var old) || old != value)
            .Select(value => value.RelativePath).ToArray();
    }

    internal static IReadOnlyCollection<string> WithKnownSidecars(IReadOnlyList<ActivatedApplicationDocument> documents,
        IEnumerable<string> selectedPaths)
    {
        var selected = selectedPaths.ToHashSet(StringComparer.Ordinal);
        var byPath = documents.ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
        foreach (var contract in documents.Where(value => ActivatedApplicationCatalogMaterializer.TryRecordKind(
                     value.RelativePath, out var kind) && kind == "mechanic"))
        {
            var sidecarPath = Path.ChangeExtension(contract.RelativePath, ".js").Replace('\\', '/');
            if (!selected.Contains(contract.RelativePath) && !selected.Contains(sidecarPath)) continue;
            if (!byPath.TryGetValue(sidecarPath, out var sidecar) || sidecar.SourceId != contract.SourceId
                || sidecar.Trust != contract.Trust || sidecar.Precedence != contract.Precedence)
                throw new ApplicationActivationException("APPLICATION_CANDIDATE_SOURCE_SPLIT", "A mechanic requires its retained same-source JavaScript sidecar.");
            selected.Add(contract.RelativePath);
            selected.Add(sidecarPath);
        }
        return selected;
    }
}
