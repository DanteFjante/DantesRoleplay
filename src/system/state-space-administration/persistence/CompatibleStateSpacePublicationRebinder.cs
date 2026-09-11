using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.StateSpaceAdministration;

/// <summary>
/// Stages state-space binding changes for the three candidate publication proofs that preserve
/// application state contracts. The caller owns the surrounding publication transaction.
/// </summary>
internal sealed class CompatibleStateSpacePublicationRebinder(
    DantesRoleplayDbContext db,
    IApplicationRegistry applications)
{
    internal Task<int> StageAsync(ApplicationCandidateCompatibleUpdateEvidence proof,
        ActiveApplicationManifest successor, CancellationToken cancellationToken) =>
        StageAsync(proof?.Basis ?? throw new ArgumentNullException(nameof(proof)), successor, cancellationToken);

    internal Task<int> StageAsync(ApplicationCandidateIntentMatchUpdateEvidence proof,
        ActiveApplicationManifest successor, CancellationToken cancellationToken) =>
        StageAsync(proof?.Basis ?? throw new ArgumentNullException(nameof(proof)), successor, cancellationToken);

    internal Task<int> StageAsync(ApplicationCandidateReviewedPureUpdateEvidence proof,
        ActiveApplicationManifest successor, CancellationToken cancellationToken) =>
        StageAsync(proof?.Basis ?? throw new ArgumentNullException(nameof(proof)), successor, cancellationToken);

    private async Task<int> StageAsync(ActiveApplicationManifest predecessor,
        ActiveApplicationManifest successor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(predecessor);
        ArgumentNullException.ThrowIfNull(successor);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Compatible state-space publication requires the owning writer transaction.");
        if (predecessor.ApplicationId != successor.ApplicationId
            || predecessor.ApplicationRevision != successor.ApplicationRevision
            || predecessor.ApplicationFingerprint != successor.ApplicationFingerprint
            || predecessor.ActivationFingerprint == successor.ActivationFingerprint)
            throw new InvalidOperationException("Compatible state-space publication must retain one exact application revision.");

        var registered = applications.Get(predecessor.ApplicationId, predecessor.ApplicationRevision)
            ?? throw new InvalidOperationException("The compatible publication application revision is unavailable.");
        if (registered.Fingerprint != predecessor.ApplicationFingerprint)
            throw new InvalidOperationException("The compatible publication application fingerprint is stale.");
        var retainedActivation = await db.Set<ApplicationActivationRevisionRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.ApplicationId == successor.ApplicationId.Value
                && value.ActivationRevision == successor.ActivationRevision, cancellationToken);
        var retainedReceipt = await db.Set<ApplicationActivationReceiptRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.OperationId == successor.ActivatedByOperationId,
                cancellationToken);
        var current = await db.Set<ApplicationActivationCurrentRecord>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.ApplicationId == successor.ApplicationId.Value,
                cancellationToken);
        if (retainedActivation is null
            || retainedActivation.ApplicationRevision != successor.ApplicationRevision
            || retainedActivation.ApplicationFingerprint != successor.ApplicationFingerprint
            || retainedActivation.ActivationFingerprint != successor.ActivationFingerprint
            || retainedActivation.ResolutionFingerprint != successor.ResolutionFingerprint
            || retainedActivation.ActivatedByOperationId != successor.ActivatedByOperationId
            || retainedReceipt is null || retainedReceipt.ApplicationId != successor.ApplicationId.Value
            || retainedReceipt.ActivationRevision != successor.ActivationRevision
            || retainedReceipt.RequestFingerprint != successor.ActivationFingerprint
            || retainedReceipt.Outcome != "activated"
            || current?.ActivationRevision != successor.ActivationRevision)
            throw new InvalidOperationException("The compatible publication activation receipt is unavailable.");

        var bindings = await db.Set<ApplicationStateSpaceRecord>()
            .Where(value => value.ApplicationId == predecessor.ApplicationId.Value
                && value.ApplicationRevision == predecessor.ApplicationRevision
                && value.ManifestFingerprint == predecessor.ActivationFingerprint
                && value.ResolutionFingerprint == predecessor.ResolutionFingerprint)
            .OrderBy(value => value.Id)
            .Select(value => new
            {
                Row = value,
                EntityCount = db.Set<ApplicationEcsEntityRecord>().Count(entity => entity.StateSpaceId == value.Id),
                ComponentCount = db.Set<ApplicationEcsComponentRecord>().Count(component => component.StateSpaceId == value.Id)
            })
            .ToArrayAsync(cancellationToken);
        if (bindings.Length == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var binding in bindings)
        {
            var row = binding.Row;
            var previousFingerprint = BindingFingerprint(row.Id, row.ApplicationId,
                row.ApplicationRevision, registered.Fingerprint, row.ManifestFingerprint,
                row.ResolutionFingerprint, row.Scope, row.BindingRevision);
            RetainBaseline(row, registered, previousFingerprint, now);

            row.ManifestFingerprint = successor.ActivationFingerprint;
            row.ResolutionFingerprint = successor.ResolutionFingerprint;
            row.BindingRevision++;
            row.UpdatedAtUtc = now;
            db.Add(new StateSpaceBindingRevisionRecord
            {
                StateSpaceId = row.Id,
                BindingRevision = row.BindingRevision,
                ApplicationId = row.ApplicationId,
                ApplicationRevision = row.ApplicationRevision,
                ApplicationFingerprint = successor.ApplicationFingerprint,
                ActiveFingerprint = successor.ActivationFingerprint,
                ResolutionFingerprint = successor.ResolutionFingerprint,
                Scope = row.Scope,
                BindingFingerprint = BindingFingerprint(row.Id, row.ApplicationId,
                    row.ApplicationRevision, successor.ApplicationFingerprint,
                    successor.ActivationFingerprint, successor.ResolutionFingerprint,
                    row.Scope, row.BindingRevision),
                PreviousBindingFingerprint = previousFingerprint,
                CompatibilityCode = "compatible-candidate-publication",
                EntityCount = binding.EntityCount,
                ComponentCount = binding.ComponentCount,
                DependencyCoverageVersion = successor.DependencyCoverageVersion,
                DependencyCoverageComplete = successor.DependencyCoverageComplete,
                // One publication can advance multiple state spaces. The exact active fingerprint
                // links each row to the retained activation and its operation without violating the
                // manual-upgrade operation ID's existing one-to-one uniqueness.
                OperationId = null,
                CreatedAtUtc = row.CreatedAtUtc,
                UpdatedAtUtc = now,
                RecordedAtUtc = now
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        return bindings.Length;
    }

    private void RetainBaseline(ApplicationStateSpaceRecord row, ApplicationRevision application,
        string bindingFingerprint, DateTime recordedAtUtc)
    {
        var existing = db.Set<StateSpaceBindingRevisionRecord>().AsNoTracking()
            .SingleOrDefault(value => value.StateSpaceId == row.Id
                && value.BindingRevision == row.BindingRevision);
        if (existing is not null)
        {
            if (existing.ApplicationId != row.ApplicationId
                || existing.ApplicationRevision != row.ApplicationRevision
                || existing.ApplicationFingerprint != application.Fingerprint
                || existing.ActiveFingerprint != row.ManifestFingerprint
                || existing.ResolutionFingerprint != row.ResolutionFingerprint
                || existing.Scope != row.Scope
                || existing.BindingFingerprint != bindingFingerprint)
                throw new InvalidOperationException("The retained state-space binding history is inconsistent.");
            return;
        }

        db.Add(new StateSpaceBindingRevisionRecord
        {
            StateSpaceId = row.Id,
            BindingRevision = row.BindingRevision,
            ApplicationId = row.ApplicationId,
            ApplicationRevision = row.ApplicationRevision,
            ApplicationFingerprint = application.Fingerprint,
            ActiveFingerprint = row.ManifestFingerprint,
            ResolutionFingerprint = row.ResolutionFingerprint,
            Scope = row.Scope,
            BindingFingerprint = bindingFingerprint,
            PreviousBindingFingerprint = null,
            CompatibilityCode = "retained-baseline",
            EntityCount = 0,
            ComponentCount = 0,
            DependencyCoverageVersion = "unknown-prior-coverage",
            DependencyCoverageComplete = false,
            OperationId = null,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc ?? row.CreatedAtUtc,
            RecordedAtUtc = row.UpdatedAtUtc ?? row.CreatedAtUtc
        });
    }

    private static string BindingFingerprint(string stateSpaceId, string applicationId,
        int applicationRevision, string applicationFingerprint, string activeFingerprint,
        string resolutionFingerprint, string scope, int bindingRevision) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            stateSpaceId,
            applicationId,
            applicationRevision,
            applicationFingerprint,
            activeFingerprint,
            resolutionFingerprint,
            scope,
            bindingRevision
        })));
}
