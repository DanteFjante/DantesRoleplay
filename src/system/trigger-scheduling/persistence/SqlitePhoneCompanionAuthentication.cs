using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

public sealed class SqlitePhoneCompanionAuthenticator(DantesRoleplayDbContext db)
    : IPhoneCompanionAuthenticator
{
    public async Task<PhoneCompanionAuthenticationResult> AuthenticateAsync(
        ApplicationIdentifier applicationId, string credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        try { PhoneCompanionIdentity.ValidateCredential(credential); }
        catch (TriggerSchedulingContractException) { return new(false); }
        var verifier = PhoneCompanionIdentity.CredentialVerifier(credential);
        var device = await db.PhoneCompanionDevices.AsNoTracking().SingleOrDefaultAsync(value =>
            value.CredentialVerifier == verifier, cancellationToken);
        if (device is null || device.ApplicationId != applicationId.Value) return new(false);
        var current = await db.PhoneCompanionDeviceCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == device.ApplicationId && value.DeviceId == device.DeviceId,
            cancellationToken);
        var active = current is not null && await db.PhoneCompanionDeviceStatuses.AsNoTracking().AnyAsync(value =>
            value.ApplicationId == device.ApplicationId && value.DeviceId == device.DeviceId &&
            value.Revision == current.CurrentRevision && value.Status == "active", cancellationToken);
        var sourceCurrent = await db.TriggerObservationSourceCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == device.ApplicationId && value.Id == device.SourceId, cancellationToken);
        if (!active || sourceCurrent?.CurrentVersion != device.SourceVersion) return new(false);
        return new(true, TrustedPrincipalContext.VerifiedPrincipal(device.PrincipalId,
            PhoneCompanionIdentity.AuthenticationMethod));
    }
}

public sealed class PhoneCompanionObservationIngestionPolicy(DantesRoleplayDbContext db)
    : IObservationIngestionPolicy
{
    public async Task ValidateAsync(TrustedPrincipalContext principal, ApplicationIdentifier applicationId,
        ObservationSubmission submission, CancellationToken cancellationToken = default)
    {
        if (principal.AuthenticationMethod != PhoneCompanionIdentity.AuthenticationMethod) return;
        var device = await db.PhoneCompanionDevices.AsNoTracking().Include(value => value.Structures)
            .SingleOrDefaultAsync(value => value.ApplicationId == applicationId.Value &&
                value.PrincipalId == principal.PrincipalId, cancellationToken);
        if (device is null || device.DeviceId != submission.Source.InstanceId ||
            device.SourceId != submission.Source.Id) throw Denied();
        var current = await db.PhoneCompanionDeviceCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == device.ApplicationId && value.DeviceId == device.DeviceId,
            cancellationToken);
        if (current is null || !await db.PhoneCompanionDeviceStatuses.AsNoTracking().AnyAsync(value =>
                value.ApplicationId == device.ApplicationId && value.DeviceId == device.DeviceId &&
                value.Revision == current.CurrentRevision && value.Status == "active", cancellationToken))
            throw Denied();
        var sourceCurrent = await db.TriggerObservationSourceCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == device.ApplicationId && value.Id == device.SourceId, cancellationToken);
        if (sourceCurrent?.CurrentVersion != device.SourceVersion) throw Denied();
        var allowed = device.Structures.SingleOrDefault(value => value.StructureId == submission.Structure.Id &&
            value.StructureVersion == submission.Structure.Version);
        if (allowed is null) throw Denied();
        var structureCurrent = await db.TriggerObservationStructureCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == device.ApplicationId && value.Id == allowed.StructureId, cancellationToken);
        var structure = await db.TriggerObservationStructures.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == device.ApplicationId && value.Id == allowed.StructureId &&
            value.Version == allowed.StructureVersion, cancellationToken);
        if (structureCurrent?.CurrentVersion != allowed.StructureVersion || structure?.Status != "active" ||
            structure.SchemaHash != allowed.StructureHash ||
            structure.DataClassification != "privacy-minimized-signal") throw Denied();
    }

    private static ObservationIngestionException Denied() => new("PHONE_SUBMISSION_DENIED",
        "The phone credential is not permitted for this observation.");
}
