using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

public sealed class SqlitePhoneCompanionRegistry(
    DantesRoleplayDbContext db,
    ITriggerClock clock,
    IPhoneCompanionCredentialGenerator credentials) : IPhoneCompanionRegistry
{
    private const int CredentialAttempts = 3;

    public async Task<PhoneCompanionRegistrationResult> RegisterAsync(
        PhoneCompanionRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var principalId = PhoneCompanionIdentity.PrincipalId(request.ApplicationId, request.DeviceId);
        await ValidateScopeAsync(request, principalId, cancellationToken);
        if (await db.PhoneCompanionDevices.AsNoTracking().AnyAsync(value =>
                value.ApplicationId == request.ApplicationId.Value && value.DeviceId == request.DeviceId,
                cancellationToken))
            throw Failure("PHONE_DEVICE_ALREADY_REGISTERED", "The phone device is already registered.");

        for (var attempt = 1; attempt <= CredentialAttempts; attempt++)
        {
            var credential = credentials.Generate();
            PhoneCompanionIdentity.ValidateCredential(credential);
            var verifier = PhoneCompanionIdentity.CredentialVerifier(credential);
            if (await db.PhoneCompanionDevices.AsNoTracking().AnyAsync(value =>
                    value.CredentialVerifier == verifier, cancellationToken))
                continue;
            var now = UtcNow();
            await using var transaction = db.Database.CurrentTransaction is null
                ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
            try
            {
                var row = new PhoneCompanionDeviceRecord
                {
                    ApplicationId = request.ApplicationId.Value, DeviceId = request.DeviceId,
                    PrincipalId = principalId, SourceId = request.SourceId,
                    SourceVersion = request.SourceVersion, CredentialVerifier = verifier,
                    PermissionProfile = "privacy-minimized-signals", CreatedAtUtc = now.UtcDateTime
                };
                for (var ordinal = 0; ordinal < request.Structures.Count; ordinal++)
                {
                    var permission = request.Structures[ordinal];
                    var structure = await db.TriggerObservationStructures.AsNoTracking().SingleAsync(value =>
                        value.ApplicationId == request.ApplicationId.Value && value.Id == permission.Id &&
                        value.Version == permission.Version, cancellationToken);
                    row.Structures.Add(new PhoneCompanionDeviceStructureRecord
                    {
                        ApplicationId = request.ApplicationId.Value, DeviceId = request.DeviceId,
                        Ordinal = ordinal, StructureId = permission.Id, StructureVersion = permission.Version,
                        StructureHash = structure.SchemaHash
                    });
                }
                row.StatusRevisions.Add(new PhoneCompanionDeviceStatusRecord
                {
                    ApplicationId = request.ApplicationId.Value, DeviceId = request.DeviceId,
                    Revision = 1, Status = "active", RecordedAtUtc = now.UtcDateTime
                });
                db.PhoneCompanionDevices.Add(row);
                db.PhoneCompanionDeviceCurrent.Add(new PhoneCompanionDeviceCurrentRecord
                {
                    ApplicationId = request.ApplicationId.Value, DeviceId = request.DeviceId,
                    CurrentRevision = 1
                });
                await db.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return new(await GetRequiredAsync(request.ApplicationId, request.DeviceId, cancellationToken),
                    credential);
            }
            catch (DbUpdateException)
            {
                if (transaction is null) throw;
                await RollbackAndClearAsync(transaction);
                if (await db.PhoneCompanionDevices.AsNoTracking().AnyAsync(value =>
                        value.ApplicationId == request.ApplicationId.Value && value.DeviceId == request.DeviceId,
                        CancellationToken.None))
                    throw Failure("PHONE_DEVICE_ALREADY_REGISTERED", "The phone device is already registered.");
                if (attempt == CredentialAttempts) throw;
            }
            catch
            {
                if (transaction is not null) await RollbackAndClearAsync(transaction);
                throw;
            }
        }
        throw Failure("PHONE_CREDENTIAL_COLLISION", "A unique phone credential could not be generated.");
    }

    public async Task<PhoneCompanionDeviceView?> RevokeAsync(ApplicationIdentifier applicationId,
        string deviceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        PhoneCompanionIdentity.ValidateDeviceId(deviceId);
        var existing = await GetAsync(applicationId, deviceId, cancellationToken);
        if (existing is null || existing.Status == PhoneCompanionStatus.Revoked) return existing;
        var now = UtcNow();
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var current = await db.PhoneCompanionDeviceCurrent.SingleAsync(value =>
                value.ApplicationId == applicationId.Value && value.DeviceId == deviceId, cancellationToken);
            if (current.CurrentRevision == 2)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    db.ChangeTracker.Clear();
                }
                return await GetAsync(applicationId, deviceId, cancellationToken);
            }
            if (current.CurrentRevision != 1) throw Failure("PHONE_STATUS_INVALID",
                "The phone device status is invalid.");
            db.PhoneCompanionDeviceStatuses.Add(new PhoneCompanionDeviceStatusRecord
            {
                ApplicationId = applicationId.Value, DeviceId = deviceId, Revision = 2,
                Status = "revoked", RecordedAtUtc = now.UtcDateTime
            });
            current.CurrentRevision = 2;
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return await GetRequiredAsync(applicationId, deviceId, cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (transaction is null) throw;
            await RollbackAndClearAsync(transaction);
            var winner = await GetAsync(applicationId, deviceId, CancellationToken.None);
            if (winner?.Status == PhoneCompanionStatus.Revoked) return winner;
            throw;
        }
        catch
        {
            if (transaction is not null) await RollbackAndClearAsync(transaction);
            throw;
        }
    }

    public async Task<PhoneCompanionDeviceView?> GetAsync(ApplicationIdentifier applicationId,
        string deviceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        PhoneCompanionIdentity.ValidateDeviceId(deviceId);
        var row = await db.PhoneCompanionDevices.AsNoTracking().Include(value => value.Structures)
            .SingleOrDefaultAsync(value => value.ApplicationId == applicationId.Value &&
                value.DeviceId == deviceId, cancellationToken);
        return row is null ? null : await ProjectAsync(row, cancellationToken);
    }

    public async Task<IReadOnlyList<PhoneCompanionDeviceView>> ListAsync(
        ApplicationIdentifier applicationId, int limit = 50, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        limit = limit <= 0 ? 50 : Math.Min(limit, 200);
        var rows = await db.PhoneCompanionDevices.AsNoTracking().Include(value => value.Structures)
            .Where(value => value.ApplicationId == applicationId.Value).OrderBy(value => value.DeviceId)
            .Take(limit).ToListAsync(cancellationToken);
        var result = new List<PhoneCompanionDeviceView>(rows.Count);
        foreach (var row in rows) result.Add(await ProjectAsync(row, cancellationToken));
        return result;
    }

    private async Task ValidateScopeAsync(PhoneCompanionRegistrationRequest request, string principalId,
        CancellationToken cancellationToken)
    {
        if (!await db.Set<ApplicationRegistryRecord>().AsNoTracking().AnyAsync(value =>
                value.Id == request.ApplicationId.Value, cancellationToken))
            throw Failure("PHONE_APPLICATION_NOT_FOUND", "The phone application is not registered.");
        var current = await db.TriggerObservationSourceCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
            value.ApplicationId == request.ApplicationId.Value && value.Id == request.SourceId,
            cancellationToken);
        if (current?.CurrentVersion != request.SourceVersion)
            throw Failure("PHONE_SOURCE_STALE", "The phone source revision is not current.");
        var source = await db.TriggerObservationSources.AsNoTracking()
            .Include(value => value.AllowedStructures).Include(value => value.AllowedPrincipals)
            .SingleOrDefaultAsync(value => value.ApplicationId == request.ApplicationId.Value &&
                value.Id == request.SourceId && value.Version == request.SourceVersion, cancellationToken);
        if (source is null || source.Status != "enabled" ||
            !source.AllowedPrincipals.Any(value => value.PrincipalId == principalId))
            throw Failure("PHONE_SOURCE_FORBIDDEN", "The current source does not permit this phone identity.");
        foreach (var permission in request.Structures)
        {
            if (!source.AllowedStructures.Any(value => value.StructureId == permission.Id &&
                    value.StructureVersion == permission.Version))
                throw Failure("PHONE_STRUCTURE_FORBIDDEN", "The phone source does not permit an exact structure.");
            var pointer = await db.TriggerObservationStructureCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
                value.ApplicationId == request.ApplicationId.Value && value.Id == permission.Id,
                cancellationToken);
            var structure = await db.TriggerObservationStructures.AsNoTracking().SingleOrDefaultAsync(value =>
                value.ApplicationId == request.ApplicationId.Value && value.Id == permission.Id &&
                value.Version == permission.Version, cancellationToken);
            if (pointer?.CurrentVersion != permission.Version || structure?.Status != "active")
                throw Failure("PHONE_STRUCTURE_STALE", "A phone structure is missing, retired, or stale.");
            if (structure.DataClassification != "privacy-minimized-signal")
                throw Failure("PHONE_STRUCTURE_PRIVACY_DENIED",
                    "Phone companions currently accept only privacy-minimized signal structures.");
        }
    }

    private async Task<PhoneCompanionDeviceView> ProjectAsync(PhoneCompanionDeviceRecord row,
        CancellationToken cancellationToken)
    {
        var current = await db.PhoneCompanionDeviceCurrent.AsNoTracking().SingleAsync(value =>
            value.ApplicationId == row.ApplicationId && value.DeviceId == row.DeviceId, cancellationToken);
        var status = await db.PhoneCompanionDeviceStatuses.AsNoTracking().SingleAsync(value =>
            value.ApplicationId == row.ApplicationId && value.DeviceId == row.DeviceId &&
            value.Revision == current.CurrentRevision, cancellationToken);
        var projected = status.Status == "revoked" ? PhoneCompanionStatus.Revoked : PhoneCompanionStatus.Active;
        if (projected == PhoneCompanionStatus.Active)
        {
            var sourceCurrent = await db.TriggerObservationSourceCurrent.AsNoTracking().SingleOrDefaultAsync(value =>
                value.ApplicationId == row.ApplicationId && value.Id == row.SourceId, cancellationToken);
            if (sourceCurrent?.CurrentVersion != row.SourceVersion)
                projected = PhoneCompanionStatus.StaleSource;
            else
            {
                foreach (var permission in row.Structures)
                {
                    var structureCurrent = await db.TriggerObservationStructureCurrent.AsNoTracking()
                        .SingleOrDefaultAsync(value => value.ApplicationId == row.ApplicationId &&
                            value.Id == permission.StructureId, cancellationToken);
                    var structure = await db.TriggerObservationStructures.AsNoTracking().SingleOrDefaultAsync(value =>
                        value.ApplicationId == row.ApplicationId && value.Id == permission.StructureId &&
                        value.Version == permission.StructureVersion, cancellationToken);
                    if (structureCurrent?.CurrentVersion != permission.StructureVersion ||
                        structure?.Status != "active" || structure.SchemaHash != permission.StructureHash ||
                        structure.DataClassification != "privacy-minimized-signal")
                    { projected = PhoneCompanionStatus.StaleStructure; break; }
                }
            }
        }
        return new(ApplicationIdentifier.Parse(row.ApplicationId), row.DeviceId, row.PrincipalId,
            row.SourceId, row.SourceVersion, PhoneCompanionPermissionProfile.PrivacyMinimizedSignals,
            row.Structures.OrderBy(value => value.Ordinal)
                .Select(value => PhoneCompanionStructurePermission.Create(value.StructureId,
                    value.StructureVersion)).ToArray(), projected, current.CurrentRevision,
            Utc(row.CreatedAtUtc), Utc(status.RecordedAtUtc));
    }

    private async Task<PhoneCompanionDeviceView> GetRequiredAsync(ApplicationIdentifier applicationId,
        string deviceId, CancellationToken cancellationToken) =>
        await GetAsync(applicationId, deviceId, cancellationToken) ??
        throw Failure("PHONE_DEVICE_NOT_FOUND", "The phone device is not registered.");

    private DateTimeOffset UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero) throw Failure("TRIGGER_CLOCK_NOT_UTC", "The trigger clock must use UTC.");
        return now;
    }
    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static TriggerSchedulingContractException Failure(string code, string message) => new(code, message);
    private async Task RollbackAndClearAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
        finally { db.ChangeTracker.Clear(); }
    }
}
