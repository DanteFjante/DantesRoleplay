using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling;

/// <summary>One transaction owner shared by the web and MCP trigger-management adapters.</summary>
public sealed class SqliteTriggerSchedulingAdministrationService(
    DantesRoleplayDbContext db,
    ITriggerSchedulingStore scheduling,
    IConditionalTriggerStore conditional,
    IObservationTriggerStore observationTriggers,
    IPhoneCompanionRegistry phones,
    ITriggerScheduleStatusReader oneTimeStatus,
    IRecurringTriggerStatusReader recurringStatus,
    IConditionalTriggerStatusReader conditionalStatus,
    IObservationTriggerStatusReader observationStatus,
    IOperationLog operations) : ITriggerSchedulingAdministrationService
{
    private const string Kind = "system.trigger-scheduling";

    public async Task<TriggerSchedulingAdministrationView> QueryAsync(
        TriggerSchedulingAdministrationQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.ApplicationId is null)
            return Empty(query.Resource, await ApplicationSummariesAsync(query.Limit, cancellationToken));

        var applicationId = query.ApplicationId;
        await RequireApplicationAsync(applicationId, cancellationToken);
        var includeOverview = query.Resource == "overview";
        var structures = includeOverview || query.Resource == "structures"
            ? await StructuresAsync(applicationId, query.Id, query.Limit, cancellationToken) : [];
        var sources = includeOverview || query.Resource == "sources"
            ? await SourcesAsync(applicationId, query.Id, query.Limit, cancellationToken) : [];
        var devices = includeOverview || query.Resource == "devices"
            ? Filter(await phones.ListAsync(applicationId, query.Limit, cancellationToken), query.Id,
                value => value.DeviceId) : [];
        var oneTime = includeOverview || query.Resource == "one-time"
            ? await StatusAsync(oneTimeStatus, applicationId, query.Id, query.Limit, cancellationToken) : [];
        var recurring = includeOverview || query.Resource == "recurring"
            ? await StatusAsync(recurringStatus, applicationId, query.Id, query.Limit, cancellationToken) : [];
        var conditionals = includeOverview || query.Resource == "conditional"
            ? await StatusAsync(conditionalStatus, applicationId, query.Id, query.Limit, cancellationToken) : [];
        var matches = includeOverview || query.Resource == "observation-triggers"
            ? await StatusAsync(observationStatus, applicationId, query.Id, query.Limit, cancellationToken) : [];
        var observations = includeOverview || query.Resource == "observations"
            ? await ObservationsAsync(applicationId, query.Id, query.Limit, cancellationToken) : [];
        var fires = includeOverview || query.Resource == "fires"
            ? await FiresAsync(applicationId, query.Id, query.Limit, cancellationToken) : [];
        PhoneCompanionPrincipalView? principal = null;
        if (query.Resource == "phone-principal")
        {
            if (query.Id is null) throw Failure("TRIGGER_ADMIN_ID_REQUIRED", "A device ID is required.");
            var deviceId = PhoneCompanionIdentity.ValidateDeviceId(query.Id);
            principal = new(applicationId, deviceId,
                PhoneCompanionIdentity.PrincipalId(applicationId, deviceId));
        }

        return new(query.Resource, [], structures, sources, devices, oneTime, recurring,
            conditionals, matches, observations, fires, principal);
    }

    public async Task<TriggerSchedulingAdministrationResult> PreviewAsync(
        TriggerSchedulingAdministrationCommand command, TriggerSchedulingAdministrationContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        var fingerprint = Fingerprint(command);
        TriggerSchedulingAdministrationResult result;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                result = await ApplyAsync(command, preview: true, cancellationToken);
            }
            finally
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
            }
        }
        var operation = await operations.RecordAsync("commit",
            $"Validated {command.Operation} without changing trigger scheduling state.", true,
            context.Intent, PreviewSubject(command.RequestToken, fingerprint), context.ProceduresUsed,
            consumesReadEvidence: false, cancellationToken: cancellationToken,
            guardEvidenceJson: JsonSerializer.Serialize(context.AuthorizationEvidence));
        return result with { Outcome = PreviewOutcome(result.Outcome), OperationId = operation.Id, Credential = null };
    }

    public async Task<TriggerSchedulingAdministrationResult> CommitAsync(
        TriggerSchedulingAdministrationCommand command, TriggerSchedulingAdministrationContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateContext(context);
        var fingerprint = Fingerprint(command);
        var existing = await operations.GetAsync(command.RequestToken, cancellationToken);
        if (existing is not null)
            return await ReplayAsync(existing, command, fingerprint, cancellationToken);
        if (!await db.Operations.AsNoTracking().AnyAsync(value => value.Tool == "commit" && value.Success &&
                value.Subject == PreviewSubject(command.RequestToken, fingerprint), cancellationToken))
            throw Failure("DRY_RUN_REQUIRED", "Preview the exact command before applying it.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await ApplyAsync(command, preview: false, cancellationToken);
            await operations.RecordAsync("commit",
                $"Applied {command.Operation} for application '{command.ApplicationId.Value}'.", true,
                context.Intent, Subject(fingerprint), context.ProceduresUsed,
                consumesReadEvidence: true, cancellationToken: cancellationToken,
                guardEvidenceJson: JsonSerializer.Serialize(context.AuthorizationEvidence),
                id: command.RequestToken);
            await transaction.CommitAsync(cancellationToken);
            return result with { OperationId = command.RequestToken };
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<TriggerSchedulingAdministrationResult> ApplyAsync(
        TriggerSchedulingAdministrationCommand command, bool preview, CancellationToken cancellationToken)
    {
        var value = ParseValue(command.Value.Json);
        return command.Operation switch
        {
            "structure.register" => Result(command, await scheduling.AppendStructureAsync(
                Structure(command.ApplicationId, value), cancellationToken)),
            "source.register" => Result(command, await scheduling.AppendSourceAsync(
                Source(command.ApplicationId, value), cancellationToken)),
            "one-time.register" => Result(command, await scheduling.AppendOneTimeTriggerAsync(
                OneTime(command.ApplicationId, value), cancellationToken)),
            "recurring.register" => Result(command, await scheduling.AppendRecurringTriggerAsync(
                Recurring(command.ApplicationId, value), cancellationToken)),
            "conditional.register" => Result(command, await conditional.AppendAsync(
                Conditional(command.ApplicationId, value), cancellationToken)),
            "observation-trigger.register" => Result(command, await observationTriggers.AppendAsync(
                ObservationTrigger(command.ApplicationId, value), cancellationToken)),
            "phone.register" => PhoneResult(command, await phones.RegisterAsync(
                PhoneRegistration(command.ApplicationId, value), cancellationToken), preview),
            "phone.revoke" => PhoneRevokeResult(command, await phones.RevokeAsync(
                command.ApplicationId, String(value, "deviceId"), cancellationToken)),
            _ => throw Failure("TRIGGER_ADMIN_OPERATION", "The trigger administration operation is unsupported.")
        };
    }

    private async Task<TriggerSchedulingAdministrationResult> ReplayAsync(Operation operation,
        TriggerSchedulingAdministrationCommand command, string fingerprint, CancellationToken cancellationToken)
    {
        if (!operation.Success || operation.Tool != "commit" || operation.Subject != Subject(fingerprint))
            throw Failure("REQUEST_TOKEN_CONFLICT",
                "That requestToken was already used by a different operation or command.");
        var value = ParseValue(command.Value.Json);
        var id = String(value, command.Operation.StartsWith("phone.", StringComparison.Ordinal) ? "deviceId" : "id");
        var exists = command.Operation switch
        {
            "structure.register" => await db.TriggerObservationStructures.AsNoTracking().AnyAsync(row =>
                row.ApplicationId == command.ApplicationId.Value && row.Id == id &&
                row.Version == Integer(value, "version"), cancellationToken),
            "source.register" => await db.TriggerObservationSources.AsNoTracking().AnyAsync(row =>
                row.ApplicationId == command.ApplicationId.Value && row.Id == id &&
                row.Version == Integer(value, "version"), cancellationToken),
            "one-time.register" => await db.OneTimeTriggers.AsNoTracking().AnyAsync(row =>
                row.ApplicationId == command.ApplicationId.Value && row.Id == id &&
                row.Version == Integer(value, "version"), cancellationToken),
            "recurring.register" => await db.RecurringTriggers.AsNoTracking().AnyAsync(row =>
                row.ApplicationId == command.ApplicationId.Value && row.Id == id &&
                row.Version == Integer(value, "version"), cancellationToken),
            "conditional.register" => await db.ConditionalTriggers.AsNoTracking().AnyAsync(row =>
                row.ApplicationId == command.ApplicationId.Value && row.Id == id &&
                row.Version == Integer(value, "version"), cancellationToken),
            "observation-trigger.register" => await db.ObservationTriggers.AsNoTracking().AnyAsync(row =>
                row.ApplicationId == command.ApplicationId.Value && row.Id == id &&
                row.Version == Integer(value, "version"), cancellationToken),
            "phone.register" => await db.PhoneCompanionDevices.AsNoTracking().AnyAsync(row =>
                row.ApplicationId == command.ApplicationId.Value && row.DeviceId == id, cancellationToken),
            "phone.revoke" => await db.PhoneCompanionDeviceStatuses.AsNoTracking().AnyAsync(row =>
                row.ApplicationId == command.ApplicationId.Value && row.DeviceId == id && row.Status == "revoked",
                cancellationToken),
            _ => false
        };
        if (!exists) throw Failure("TRIGGER_ADMIN_INCONSISTENT",
            "The prior successful receipt no longer has its immutable trigger-scheduling record.");
        var version = value.TryGetProperty("version", out var versionValue) ? versionValue.GetInt32() : (int?)null;
        return new(command.Operation, command.ApplicationId, "replay", operation.Id,
            JsonSerializer.SerializeToElement(new { id, version }), null);
    }

    private static TriggerSchedulingAdministrationResult Result<T>(
        TriggerSchedulingAdministrationCommand command, TriggerSchedulingWriteResult<T> result)
    {
        if (result.Disposition == TriggerSchedulingWriteDisposition.Conflict || result.Value is null)
            throw Failure(result.Code, "The immutable ID and version already contain different content.");
        var value = JsonSerializer.SerializeToElement(result.Value);
        var outcome = result.Disposition == TriggerSchedulingWriteDisposition.Appended ? "registered" : "unchanged";
        return new(command.Operation, command.ApplicationId, outcome, "", value);
    }

    private static TriggerSchedulingAdministrationResult PhoneResult(
        TriggerSchedulingAdministrationCommand command, PhoneCompanionRegistrationResult result, bool preview) =>
        new(command.Operation, command.ApplicationId, "registered", "",
            JsonSerializer.SerializeToElement(result.Device), preview ? null : result.Credential);

    private static TriggerSchedulingAdministrationResult PhoneRevokeResult(
        TriggerSchedulingAdministrationCommand command, PhoneCompanionDeviceView? result)
    {
        if (result is null) throw Failure("PHONE_DEVICE_NOT_FOUND", "The phone device is not registered.");
        return new(command.Operation, command.ApplicationId, "revoked", "",
            JsonSerializer.SerializeToElement(result));
    }

    private static ObservationStructureDefinition Structure(ApplicationIdentifier applicationId, JsonElement value)
    {
        Exact(value, "id", "version", "normalizedSchema", "description", "status", "dataClassification");
        var schema = String(value, "normalizedSchema");
        return ObservationStructureDefinition.Create(applicationId, String(value, "id"), Integer(value, "version"),
            SystemJsonSchemaProfile.Version2Id, schema,
            TriggerSchedulingFingerprint.Sha256(Encoding.UTF8.GetBytes(schema)), String(value, "description"),
            EnumValue(String(value, "status"), ("active", ObservationStructureStatus.Active),
                ("retired", ObservationStructureStatus.Retired)),
            EnumValue(String(value, "dataClassification"),
                ("general", ObservationDataClassification.General),
                ("privacy-minimized-signal", ObservationDataClassification.PrivacyMinimizedSignal),
                ("raw-location", ObservationDataClassification.RawLocation),
                ("third-party-notification-content", ObservationDataClassification.ThirdPartyNotificationContent)));
    }

    private static ObservationSourceDefinition Source(ApplicationIdentifier applicationId, JsonElement value)
    {
        Exact(value, "id", "version", "status", "structures", "principalIds", "replayWindowSeconds", "requestsPerMinute");
        return ObservationSourceDefinition.Create(applicationId, String(value, "id"), Integer(value, "version"),
            EnumValue(String(value, "status"), ("enabled", ObservationSourceStatus.Enabled),
                ("disabled", ObservationSourceStatus.Disabled)),
            Array(value, "structures").Select(StructureReference).ToArray(),
            Array(value, "principalIds").Select(item => RequiredString(item)).ToArray(),
            TimeSpan.FromSeconds(Integer(value, "replayWindowSeconds")), Integer(value, "requestsPerMinute"));
    }

    private static OneTimeTriggerDefinition OneTime(ApplicationIdentifier applicationId, JsonElement value)
    {
        Exact(value, "id", "version", "dueAtUtc", "misfirePolicy", "lifecycle", "notification");
        return OneTimeTriggerDefinition.Create(applicationId, String(value, "id"), Integer(value, "version"),
            Utc(String(value, "dueAtUtc")), Misfire(String(value, "misfirePolicy")),
            TriggerFireTarget.NotificationOnly,
            EnumValue(String(value, "lifecycle"), ("active", TriggerLifecycle.Active),
                ("cancelled", TriggerLifecycle.Cancelled)), Notification(Object(value, "notification")));
    }

    private static RecurringTriggerDefinition Recurring(ApplicationIdentifier applicationId, JsonElement value)
    {
        Exact(value, "id", "version", "lifecycle", "misfirePolicy", "pattern", "notification");
        var pattern = Object(value, "pattern");
        Exact(pattern, "kind", "interval", "localTime", "timeZoneId", "startDate", "endDate",
            "weekdays", "dayOfMonth", "gapPolicy", "overlapPolicy");
        var kind = String(pattern, "kind");
        var interval = Integer(pattern, "interval");
        var localTime = TimeOnlyValue(String(pattern, "localTime"));
        var zone = String(pattern, "timeZoneId");
        var start = NullableDate(pattern, "startDate");
        var end = NullableDate(pattern, "endDate");
        var gap = EnumValue(String(pattern, "gapPolicy"), ("skip", RecurrenceGapPolicy.Skip),
            ("next-valid", RecurrenceGapPolicy.NextValid));
        var overlap = EnumValue(String(pattern, "overlapPolicy"), ("earlier", RecurrenceOverlapPolicy.Earlier),
            ("later", RecurrenceOverlapPolicy.Later));
        var recurrence = kind switch
        {
            "daily" => RecurrencePattern.Daily(interval, localTime, zone, start, end, gap, overlap),
            "weekly" => RecurrencePattern.Weekly(interval, localTime, zone,
                Array(pattern, "weekdays").Select(item => EnumValue(RequiredString(item),
                    ("sunday", DayOfWeek.Sunday), ("monday", DayOfWeek.Monday),
                    ("tuesday", DayOfWeek.Tuesday), ("wednesday", DayOfWeek.Wednesday),
                    ("thursday", DayOfWeek.Thursday), ("friday", DayOfWeek.Friday),
                    ("saturday", DayOfWeek.Saturday))).ToArray(), start, end, gap, overlap),
            "monthly" => RecurrencePattern.Monthly(interval, localTime, zone,
                Integer(pattern, "dayOfMonth"), start, end, gap, overlap),
            _ => throw Failure("TRIGGER_ADMIN_VALUE", "The recurrence kind is invalid.")
        };
        return RecurringTriggerDefinition.Create(applicationId, String(value, "id"), Integer(value, "version"),
            recurrence, EnumValue(String(value, "lifecycle"), ("active", RecurringTriggerLifecycle.Active),
                ("paused", RecurringTriggerLifecycle.Paused), ("cancelled", RecurringTriggerLifecycle.Cancelled)),
            Misfire(String(value, "misfirePolicy")), TriggerFireTarget.NotificationOnly,
            Notification(Object(value, "notification")));
    }

    private static ConditionalTriggerDefinition Conditional(ApplicationIdentifier applicationId, JsonElement value)
    {
        Exact(value, "id", "version", "lifecycle", "kind", "activation", "rearm", "stateSpaceId",
            "dependencies", "adapter", "adapterConfiguration", "notification");
        var adapter = Object(value, "adapter");
        Exact(adapter, "id", "version");
        return ConditionalTriggerDefinition.Create(applicationId, String(value, "id"), Integer(value, "version"),
            EnumValue(String(value, "lifecycle"), ("active", ConditionalTriggerLifecycle.Active),
                ("paused", ConditionalTriggerLifecycle.Paused), ("cancelled", ConditionalTriggerLifecycle.Cancelled)),
            EnumValue(String(value, "kind"), ("world-clock-threshold", ConditionalTriggerKind.WorldClockThreshold),
                ("state-condition", ConditionalTriggerKind.StateCondition)),
            EnumValue(String(value, "activation"), ("rising-edge", ConditionalTriggerActivation.RisingEdge),
                ("level", ConditionalTriggerActivation.Level)),
            EnumValue(String(value, "rearm"), ("on-false", ConditionalTriggerRearm.OnFalse),
                ("manual", ConditionalTriggerRearm.Manual)), String(value, "stateSpaceId"),
            Array(value, "dependencies").Select(Dependency).ToArray(),
            ConditionalTriggerAdapterReference.Create(String(adapter, "id"), Integer(adapter, "version")),
            Object(value, "adapterConfiguration").GetRawText(), TriggerFireTarget.NotificationOnly,
            Notification(Object(value, "notification")));
    }

    private static ObservationTriggerDefinition ObservationTrigger(ApplicationIdentifier applicationId,
        JsonElement value)
    {
        Exact(value, "id", "version", "lifecycle", "sourceId", "sourceVersion", "structureId",
            "structureVersion", "structureHash", "adapter", "adapterConfiguration", "notification");
        var adapter = Object(value, "adapter");
        Exact(adapter, "id", "version");
        return ObservationTriggerDefinition.Create(applicationId, String(value, "id"), Integer(value, "version"),
            EnumValue(String(value, "lifecycle"), ("active", ObservationTriggerLifecycle.Active),
                ("paused", ObservationTriggerLifecycle.Paused), ("cancelled", ObservationTriggerLifecycle.Cancelled)),
            String(value, "sourceId"), Integer(value, "sourceVersion"), String(value, "structureId"),
            Integer(value, "structureVersion"), String(value, "structureHash"),
            ObservationMatchAdapterReference.Create(String(adapter, "id"), Integer(adapter, "version")),
            Object(value, "adapterConfiguration").GetRawText(), TriggerFireTarget.NotificationOnly,
            Notification(Object(value, "notification")));
    }

    private static PhoneCompanionRegistrationRequest PhoneRegistration(ApplicationIdentifier applicationId,
        JsonElement value)
    {
        Exact(value, "deviceId", "sourceId", "sourceVersion", "structures");
        return PhoneCompanionRegistrationRequest.Create(applicationId, String(value, "deviceId"),
            String(value, "sourceId"), Integer(value, "sourceVersion"),
            Array(value, "structures").Select(item =>
            {
                Exact(item, "id", "version");
                return PhoneCompanionStructurePermission.Create(String(item, "id"), Integer(item, "version"));
            }).ToArray());
    }

    private static TriggerNotificationTarget Notification(JsonElement value)
    {
        Exact(value, "topic", "subject", "body", "stateSpaceId", "entityIds");
        return TriggerNotificationTarget.Create(String(value, "topic"), String(value, "subject"),
            String(value, "body"), NullableString(value, "stateSpaceId"),
            Array(value, "entityIds").Select(RequiredString).ToArray());
    }

    private static ConditionalTriggerDependency Dependency(JsonElement value)
    {
        Exact(value, "entityId", "qualifiedTypeId", "typeVersion", "schemaHash");
        return ConditionalTriggerDependency.Create(String(value, "entityId"),
            new EcsComponentReference(String(value, "qualifiedTypeId"), Integer(value, "typeVersion"),
                String(value, "schemaHash")));
    }

    private static ObservationStructureReference StructureReference(JsonElement value)
    {
        Exact(value, "id", "version");
        return ObservationStructureReference.Create(String(value, "id"), Integer(value, "version"));
    }

    private async Task<IReadOnlyList<TriggerSchedulingApplicationSummary>> ApplicationSummariesAsync(
        int limit, CancellationToken cancellationToken)
    {
        var applications = await db.Set<ApplicationRegistryRecord>().AsNoTracking().OrderBy(value => value.Id)
            .Take(limit).ToArrayAsync(cancellationToken);
        var result = new List<TriggerSchedulingApplicationSummary>(applications.Length);
        foreach (var application in applications)
        {
            var id = application.Id;
            result.Add(new(ApplicationIdentifier.Parse(id), application.DisplayName,
                await db.TriggerObservationStructureCurrent.CountAsync(value => value.ApplicationId == id, cancellationToken),
                await db.TriggerObservationSourceCurrent.CountAsync(value => value.ApplicationId == id, cancellationToken),
                await db.PhoneCompanionDevices.CountAsync(value => value.ApplicationId == id, cancellationToken),
                await db.OneTimeTriggerCurrent.CountAsync(value => value.ApplicationId == id, cancellationToken),
                await db.RecurringTriggerCurrent.CountAsync(value => value.ApplicationId == id, cancellationToken),
                await db.ConditionalTriggerCurrent.CountAsync(value => value.ApplicationId == id, cancellationToken),
                await db.ObservationTriggerCurrent.CountAsync(value => value.ApplicationId == id, cancellationToken),
                await db.TriggerObservations.CountAsync(value => value.ApplicationId == id, cancellationToken)));
        }
        return result;
    }

    private async Task<IReadOnlyList<StoredObservationStructure>> StructuresAsync(ApplicationIdentifier applicationId,
        string? id, int limit, CancellationToken cancellationToken)
    {
        var rows = await db.TriggerObservationStructures.AsNoTracking()
            .Where(value => value.ApplicationId == applicationId.Value && (id == null || value.Id == id) &&
                db.TriggerObservationStructureCurrent.Any(current => current.ApplicationId == value.ApplicationId &&
                    current.Id == value.Id && current.CurrentVersion == value.Version))
            .OrderBy(value => value.Id).Take(limit).ToArrayAsync(cancellationToken);
        return rows.Select(value => new StoredObservationStructure(applicationId, value.Id, value.Version,
            value.SchemaProfileId, value.NormalizedSchema, value.SchemaHash, value.Description,
            value.Status == "active" ? ObservationStructureStatus.Active : ObservationStructureStatus.Retired,
            SqliteTriggerSchedulingStore.ParseDataClassification(value.DataClassification), Utc(value.RecordedAtUtc))).ToArray();
    }

    private async Task<IReadOnlyList<StoredObservationSource>> SourcesAsync(ApplicationIdentifier applicationId,
        string? id, int limit, CancellationToken cancellationToken)
    {
        var rows = await db.TriggerObservationSources.AsNoTracking().Include(value => value.AllowedStructures)
            .Include(value => value.AllowedPrincipals)
            .Where(value => value.ApplicationId == applicationId.Value && (id == null || value.Id == id) &&
                db.TriggerObservationSourceCurrent.Any(current => current.ApplicationId == value.ApplicationId &&
                    current.Id == value.Id && current.CurrentVersion == value.Version))
            .OrderBy(value => value.Id).Take(limit).ToArrayAsync(cancellationToken);
        return rows.Select(value => new StoredObservationSource(applicationId, value.Id, value.Version,
            value.Status == "enabled" ? ObservationSourceStatus.Enabled : ObservationSourceStatus.Disabled,
            value.AllowedStructures.OrderBy(item => item.StructureId).ThenBy(item => item.StructureVersion)
                .Select(item => ObservationStructureReference.Create(item.StructureId, item.StructureVersion)).ToArray(),
            value.AllowedPrincipals.OrderBy(item => item.PrincipalId).Select(item => item.PrincipalId).ToArray(),
            TimeSpan.FromSeconds(value.ReplayWindowSeconds), value.RequestsPerMinute, Utc(value.RecordedAtUtc))).ToArray();
    }

    private async Task<IReadOnlyList<TriggerObservationAdministrationView>> ObservationsAsync(
        ApplicationIdentifier applicationId, string? id, int limit, CancellationToken cancellationToken)
    {
        var rows = await db.TriggerObservations.AsNoTracking().Where(value =>
                value.ApplicationId == applicationId.Value && (id == null || value.Id == id))
            .OrderByDescending(value => value.ReceivedAtUtc).Take(limit).ToArrayAsync(cancellationToken);
        return rows.Select(value => new TriggerObservationAdministrationView(value.Id, applicationId,
            value.SourceId, value.SourceVersion, value.SourceInstanceId, value.OccurrenceId,
            value.StructureId, value.StructureVersion, value.StructureHash, Utc(value.ObservedAtUtc),
            Utc(value.ReceivedAtUtc), value.DataHash, value.PrincipalId)).ToArray();
    }

    private async Task<IReadOnlyList<TriggerFireAdministrationView>> FiresAsync(
        ApplicationIdentifier applicationId, string? id, int limit, CancellationToken cancellationToken)
    {
        var result = new List<TriggerFireAdministrationView>();
        var oneLinks = await db.TriggerNotificationLinks.AsNoTracking().Where(value =>
            value.ApplicationId == applicationId.Value).ToDictionaryAsync(value => value.FireId,
            value => value.NotificationId, cancellationToken);
        var one = await db.TriggerFireReceipts.AsNoTracking().Where(value =>
                value.ApplicationId == applicationId.Value && (id == null || value.Id == id))
            .OrderByDescending(value => value.RecordedAtUtc).Take(limit).ToArrayAsync(cancellationToken);
        result.AddRange(one.Select(value => Fire("one-time", value.Id, applicationId, value.TriggerId,
            value.TriggerVersion, value.OccurrenceAtUtc, value.Disposition, oneLinks.GetValueOrDefault(value.Id),
            value.RecordedAtUtc)));
        var recurringLinks = await db.RecurringTriggerNotificationLinks.AsNoTracking().Where(value =>
            value.ApplicationId == applicationId.Value).ToDictionaryAsync(value => value.FireId,
            value => value.NotificationId, cancellationToken);
        var recurring = await db.RecurringTriggerFireReceipts.AsNoTracking().Where(value =>
                value.ApplicationId == applicationId.Value && (id == null || value.Id == id))
            .OrderByDescending(value => value.RecordedAtUtc).Take(limit).ToArrayAsync(cancellationToken);
        result.AddRange(recurring.Select(value => Fire("recurring", value.Id, applicationId, value.TriggerId,
            value.TriggerVersion, value.OccurrenceAtUtc, value.Disposition,
            recurringLinks.GetValueOrDefault(value.Id), value.RecordedAtUtc)));
        var conditionalLinks = await db.ConditionalTriggerNotificationLinks.AsNoTracking().Where(value =>
            value.ApplicationId == applicationId.Value).ToDictionaryAsync(value => value.FireId,
            value => value.NotificationId, cancellationToken);
        var conditionals = await db.ConditionalTriggerFireReceipts.AsNoTracking().Where(value =>
                value.ApplicationId == applicationId.Value && (id == null || value.Id == id))
            .OrderByDescending(value => value.RecordedAtUtc).Take(limit).ToArrayAsync(cancellationToken);
        result.AddRange(conditionals.Select(value => Fire("conditional", value.Id, applicationId,
            value.TriggerId, value.TriggerVersion, value.RecordedAtUtc, value.Disposition,
            conditionalLinks.GetValueOrDefault(value.Id), value.RecordedAtUtc)));
        var observationLinks = await db.ObservationTriggerNotificationLinks.AsNoTracking().Where(value =>
            value.ApplicationId == applicationId.Value).ToDictionaryAsync(value => value.FireId,
            value => value.NotificationId, cancellationToken);
        var observations = await db.ObservationTriggerMatchReceipts.AsNoTracking().Where(value =>
                value.ApplicationId == applicationId.Value && (id == null || value.Id == id))
            .OrderByDescending(value => value.RecordedAtUtc).Take(limit).ToArrayAsync(cancellationToken);
        result.AddRange(observations.Select(value => Fire("observation", value.Id, applicationId,
            value.TriggerId, value.TriggerVersion, value.RecordedAtUtc, value.Disposition,
            observationLinks.GetValueOrDefault(value.Id), value.RecordedAtUtc)));
        return result.OrderByDescending(value => value.RecordedAt).Take(limit).ToArray();
    }

    private static TriggerFireAdministrationView Fire(string kind, string id, ApplicationIdentifier applicationId,
        string triggerId, int version, DateTime occurrenceAt, string disposition, string? notificationId,
        DateTime recordedAt) => new(kind, id, applicationId, triggerId, version, Utc(occurrenceAt), disposition,
            notificationId, Utc(recordedAt));

    private static async Task<IReadOnlyList<TriggerScheduleStatusView>> StatusAsync(
        ITriggerScheduleStatusReader reader, ApplicationIdentifier applicationId, string? id, int limit,
        CancellationToken cancellationToken) => id is null
        ? await reader.ListAsync(applicationId, limit, cancellationToken)
        : Optional(await reader.GetAsync(applicationId, id, cancellationToken: cancellationToken));

    private static async Task<IReadOnlyList<RecurringTriggerStatusView>> StatusAsync(
        IRecurringTriggerStatusReader reader, ApplicationIdentifier applicationId, string? id, int limit,
        CancellationToken cancellationToken) => id is null
        ? await reader.ListAsync(applicationId, limit, cancellationToken)
        : Optional(await reader.GetAsync(applicationId, id, cancellationToken: cancellationToken));

    private static async Task<IReadOnlyList<ConditionalTriggerStatusView>> StatusAsync(
        IConditionalTriggerStatusReader reader, ApplicationIdentifier applicationId, string? id, int limit,
        CancellationToken cancellationToken) => id is null
        ? await reader.ListAsync(applicationId, limit, cancellationToken)
        : Optional(await reader.GetAsync(applicationId, id, cancellationToken: cancellationToken));

    private static async Task<IReadOnlyList<ObservationTriggerStatusView>> StatusAsync(
        IObservationTriggerStatusReader reader, ApplicationIdentifier applicationId, string? id, int limit,
        CancellationToken cancellationToken) => id is null
        ? await reader.ListAsync(applicationId, limit, cancellationToken)
        : Optional(await reader.GetAsync(applicationId, id, cancellationToken: cancellationToken));

    private static IReadOnlyList<T> Optional<T>(T? value) where T : class => value is null ? [] : [value];
    private static IReadOnlyList<T> Filter<T>(IReadOnlyList<T> values, string? id, Func<T, string> selector) =>
        id is null ? values : values.Where(value => selector(value) == id).ToArray();

    private async Task RequireApplicationAsync(ApplicationIdentifier applicationId,
        CancellationToken cancellationToken)
    {
        if (!await db.Set<ApplicationRegistryRecord>().AsNoTracking().AnyAsync(value =>
                value.Id == applicationId.Value, cancellationToken))
            throw Failure("APPLICATION_UNKNOWN", "The application is not registered.");
    }

    private static TriggerSchedulingAdministrationView Empty(string resource,
        IReadOnlyList<TriggerSchedulingApplicationSummary> applications) =>
        new(resource, applications, [], [], [], [], [], [], [], [], [], null);

    private static void ValidateContext(TriggerSchedulingAdministrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.AuthorizationEvidence.Allowed ||
            context.AuthorizationEvidence.Capability != "trigger.admin.write")
            throw Failure("PRIVATE_OPERATOR_DENIED", "Trigger administration write authorization is required.");
    }

    private static string Fingerprint(TriggerSchedulingAdministrationCommand command) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{command.Operation}\n{command.ApplicationId.Value}\n{command.Value.Json}")));
    private static string Subject(string fingerprint) => $"{Kind}|{fingerprint}";
    private static string PreviewSubject(string token, string fingerprint) => $"preview|{Kind}|{token}|{fingerprint}";
    private static string PreviewOutcome(string value) => value switch
    {
        "registered" => "would-register", "revoked" => "would-revoke", _ => value
    };

    private static JsonElement ParseValue(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static void Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal)
                .SetEquals(names))
            throw Failure("TRIGGER_ADMIN_VALUE", "The command value has missing or unsupported fields.");
    }

    private static JsonElement Property(JsonElement value, string name, JsonValueKind kind)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != kind)
            throw Failure("TRIGGER_ADMIN_VALUE", $"The command value field '{name}' is invalid.");
        return property;
    }

    private static string String(JsonElement value, string name) => RequiredString(Property(value, name, JsonValueKind.String));
    private static string RequiredString(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : throw Failure("TRIGGER_ADMIN_VALUE", "A command value string is invalid.");
    private static string? NullableString(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return null;
        return RequiredString(property);
    }
    private static int Integer(JsonElement value, string name)
    {
        var property = Property(value, name, JsonValueKind.Number);
        if (!property.TryGetInt32(out var result)) throw Failure("TRIGGER_ADMIN_VALUE", $"The field '{name}' must be an integer.");
        return result;
    }
    private static JsonElement Object(JsonElement value, string name) => Property(value, name, JsonValueKind.Object);
    private static JsonElement.ArrayEnumerator Array(JsonElement value, string name) =>
        Property(value, name, JsonValueKind.Array).EnumerateArray();
    private static T EnumValue<T>(string value, params (string Name, T Value)[] allowed) where T : struct =>
        allowed.FirstOrDefault(item => item.Name == value) is var match && match.Name is not null
            ? match.Value : throw Failure("TRIGGER_ADMIN_VALUE", $"The value '{value}' is unsupported.");
    private static TriggerMisfirePolicy Misfire(string value) => EnumValue(value,
        ("skip", TriggerMisfirePolicy.Skip), ("fire-once", TriggerMisfirePolicy.FireOnce));
    private static DateTimeOffset Utc(string value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result) ||
            result.Offset != TimeSpan.Zero) throw Failure("TRIGGER_ADMIN_VALUE", "The timestamp must be RFC 3339 UTC.");
        return result;
    }
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static TimeOnly TimeOnlyValue(string value) => TimeOnly.TryParseExact(value, "HH:mm:ss",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ? result
        : throw Failure("TRIGGER_ADMIN_VALUE", "localTime must use HH:mm:ss.");
    private static DateOnly? NullableDate(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return null;
        return DateOnly.TryParseExact(RequiredString(property), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var result) ? result
            : throw Failure("TRIGGER_ADMIN_VALUE", $"The field '{name}' must use yyyy-MM-dd.");
    }
    private static TriggerSchedulingAdministrationException Failure(string code, string message) => new(code, message);
}
