using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.SchemaValidation;
using static DantesRoleplay.TriggerScheduling.TriggerSchedulingFailures;

namespace DantesRoleplay.TriggerScheduling;

public sealed class TriggerSchedulingContractException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public enum ObservationSourceStatus
{
    Enabled,
    Disabled
}

public enum ObservationStructureStatus
{
    Active,
    Retired
}

public enum TriggerMisfirePolicy
{
    Skip,
    FireOnce
}

public enum TriggerFireTarget
{
    NotificationOnly
}

public enum TriggerLifecycle
{
    Active,
    Cancelled
}

public sealed record TriggerNotificationTarget
{
    private TriggerNotificationTarget(
        string topic,
        string subject,
        string body,
        string? stateSpaceId,
        IReadOnlyList<string> entityIds)
    {
        Topic = RequireText(topic, nameof(topic), 200, "TRIGGER_NOTIFICATION_TOPIC");
        Subject = RequireText(subject, nameof(subject), 400, "TRIGGER_NOTIFICATION_SUBJECT");
        Body = (body ?? string.Empty).Trim();
        if (Encoding.UTF8.GetByteCount(Body) > TriggerSchedulingLimits.MaximumStringBytes)
            throw Failure("TRIGGER_NOTIFICATION_BODY", "The notification body exceeds the configured bound.");
        if (stateSpaceId is not null)
        {
            StateSpaceId = RequireText(stateSpaceId, nameof(stateSpaceId), 200, "TRIGGER_NOTIFICATION_STATE_SPACE");
            if (entityIds.Count == 0)
                throw Failure("TRIGGER_NOTIFICATION_LINKS", "A notification state space requires at least one entity link.");
        }
        else if (entityIds.Count != 0)
        {
            throw Failure("TRIGGER_NOTIFICATION_LINKS", "Notification entity links require an exact state space.");
        }
        if (entityIds.Count > 32 || entityIds.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Any(char.IsControl)) ||
            entityIds.Distinct(StringComparer.Ordinal).Count() != entityIds.Count)
            throw Failure("TRIGGER_NOTIFICATION_LINKS", "Notification entity links must be distinct bounded IDs.");
        EntityIds = Array.AsReadOnly(entityIds.ToArray());
    }

    public string Topic { get; }
    public string Subject { get; }
    public string Body { get; }
    public string? StateSpaceId { get; }
    public IReadOnlyList<string> EntityIds { get; }

    public static TriggerNotificationTarget Create(
        string topic,
        string subject,
        string body = "",
        string? stateSpaceId = null,
        IReadOnlyList<string>? entityIds = null) =>
        new(topic, subject, body, stateSpaceId, entityIds ?? []);

    internal static TriggerNotificationTarget Default(string triggerId) =>
        new("scheduled.reminder", "Scheduled reminder", triggerId, null, []);

    private static string RequireText(string value, string name, int maximum, string code)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maximum || normalized.Any(char.IsControl))
            throw Failure(code, $"The notification {name} is invalid.");
        return normalized;
    }
}

public enum OneTimeTriggerDisposition
{
    Pending,
    Due,
    Missed
}

public static class TriggerSchedulingLimits
{
    public const int MaximumRequestBytes = 64 * 1024;
    public const int MaximumJsonDepth = 16;
    public const int MaximumJsonNodes = 512;
    public const int MaximumObjectProperties = 256;
    public const int MaximumArrayItems = 256;
    public const int MaximumStringBytes = 16 * 1024;
    public const int MaximumReplayWindowDays = 7;
    public const int MaximumFutureSkewMinutes = 5;
    public const int MaximumFireOnceLatenessHours = 24;
}

public sealed record ObservationSourceReference
{
    private ObservationSourceReference(string id, string instanceId, string occurrenceId)
    {
        Id = TriggerSchedulingIdentifier.Qualified(id, nameof(id));
        InstanceId = TriggerSchedulingIdentifier.OccurrencePart(instanceId, nameof(instanceId), 128);
        OccurrenceId = TriggerSchedulingIdentifier.OccurrencePart(occurrenceId, nameof(occurrenceId), 200);
    }

    public string Id { get; }
    public string InstanceId { get; }
    public string OccurrenceId { get; }

    public static ObservationSourceReference Create(string id, string instanceId, string occurrenceId) =>
        new(id, instanceId, occurrenceId);
}

public sealed record ObservationStructureReference
{
    private ObservationStructureReference(string id, int version)
    {
        Id = TriggerSchedulingIdentifier.Qualified(id, nameof(id));
        if (version < 1) throw Failure("INVALID_STRUCTURE_VERSION", "The structure version must be positive.");
        Version = version;
    }

    public string Id { get; }
    public int Version { get; }

    public static ObservationStructureReference Create(string id, int version) => new(id, version);
}

public sealed record CanonicalObservationData(
    string Json,
    string Hash,
    int NodeCount,
    int PropertyCount,
    int ArrayItemCount);

public static class ObservationDataCanonicalizer
{
    public static CanonicalObservationData ParseObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw Failure("OBSERVATION_DATA_INVALID_JSON", "Observation data must be a JSON object.");
        if (Encoding.UTF8.GetByteCount(json) > TriggerSchedulingLimits.MaximumRequestBytes)
            throw Failure("OBSERVATION_DATA_TOO_LARGE", "Observation data exceeds the configured bound.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = TriggerSchedulingLimits.MaximumJsonDepth
            });
        }
        catch (JsonException)
        {
            throw Failure("OBSERVATION_DATA_INVALID_JSON", "Observation data must be valid JSON within the configured depth.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw Failure("OBSERVATION_DATA_ROOT", "Observation data must have an object root.");

            var output = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(output))
            {
                var state = new CanonicalizationState(writer);
                state.Write(document.RootElement, 1);
                writer.Flush();
                return new(
                    Encoding.UTF8.GetString(output.WrittenSpan),
                    TriggerSchedulingFingerprint.Sha256(output.WrittenSpan),
                    state.NodeCount,
                    state.PropertyCount,
                    state.ArrayItemCount);
            }
        }
    }

    private sealed class CanonicalizationState(Utf8JsonWriter writer)
    {
        private readonly Utf8JsonWriter writer = writer;

        public int NodeCount { get; private set; }
        public int PropertyCount { get; private set; }
        public int ArrayItemCount { get; private set; }

        public void Write(JsonElement value, int depth)
        {
            if (depth > TriggerSchedulingLimits.MaximumJsonDepth)
                throw Failure("OBSERVATION_DATA_DEPTH", "Observation data exceeds the configured depth.");
            if (++NodeCount > TriggerSchedulingLimits.MaximumJsonNodes)
                throw Failure("OBSERVATION_DATA_NODES", "Observation data exceeds the configured node bound.");

            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    WriteObject(value, depth);
                    return;
                case JsonValueKind.Array:
                    WriteArray(value, depth);
                    return;
                case JsonValueKind.String:
                    var text = value.GetString() ?? string.Empty;
                    if (Encoding.UTF8.GetByteCount(text) > TriggerSchedulingLimits.MaximumStringBytes)
                        throw Failure("OBSERVATION_DATA_STRING", "Observation data contains a string exceeding the configured bound.");
                    writer.WriteStringValue(text);
                    return;
                case JsonValueKind.Number:
                    WriteNumber(value);
                    return;
                case JsonValueKind.True:
                    writer.WriteBooleanValue(true);
                    return;
                case JsonValueKind.False:
                    writer.WriteBooleanValue(false);
                    return;
                case JsonValueKind.Null:
                    writer.WriteNullValue();
                    return;
                default:
                    throw Failure("OBSERVATION_DATA_KIND", "Observation data contains an unsupported JSON value.");
            }
        }

        private void WriteObject(JsonElement value, int depth)
        {
            var properties = value.EnumerateObject().ToArray();
            foreach (var property in properties)
            {
                if (++PropertyCount > TriggerSchedulingLimits.MaximumObjectProperties)
                    throw Failure("OBSERVATION_DATA_PROPERTIES", "Observation data exceeds the configured property bound.");
                if (Encoding.UTF8.GetByteCount(property.Name) > TriggerSchedulingLimits.MaximumStringBytes)
                    throw Failure("OBSERVATION_DATA_STRING", "Observation data contains a property name exceeding the configured bound.");
            }

            var duplicate = properties.GroupBy(property => property.Name, StringComparer.Ordinal).Any(group => group.Skip(1).Any());
            if (duplicate)
                throw Failure("OBSERVATION_DATA_DUPLICATE_PROPERTY", "Observation data must not contain duplicate property names.");

            Array.Sort(properties, static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
            writer.WriteStartObject();
            foreach (var property in properties)
            {
                writer.WritePropertyName(property.Name);
                Write(property.Value, depth + 1);
            }

            writer.WriteEndObject();
        }

        private void WriteArray(JsonElement value, int depth)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
            {
                if (++ArrayItemCount > TriggerSchedulingLimits.MaximumArrayItems)
                    throw Failure("OBSERVATION_DATA_ARRAY_ITEMS", "Observation data exceeds the configured array-item bound.");
                Write(item, depth + 1);
            }

            writer.WriteEndArray();
        }

        private void WriteNumber(JsonElement value)
        {
            var raw = value.GetRawText();
            if (value.TryGetInt64(out var integer))
            {
                writer.WriteNumberValue(integer);
                return;
            }

            if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
            {
                writer.WriteNumberValue(decimalValue);
                return;
            }

            throw Failure("OBSERVATION_DATA_NUMBER", "Observation data contains a number outside the deterministic decimal range.");
        }
    }
}

public sealed record ObservationSubmission
{
    private ObservationSubmission(
        string requestId,
        ObservationSourceReference source,
        ObservationStructureReference structure,
        DateTimeOffset observedAt,
        CanonicalObservationData data)
    {
        RequestId = TriggerSchedulingIdentifier.RequestId(requestId);
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Structure = structure ?? throw new ArgumentNullException(nameof(structure));
        if (observedAt.Offset != TimeSpan.Zero)
            throw Failure("OBSERVATION_TIME_NOT_UTC", "The observation time must be UTC.");
        ObservedAt = observedAt;
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public string RequestId { get; }
    public ObservationSourceReference Source { get; }
    public ObservationStructureReference Structure { get; }
    public DateTimeOffset ObservedAt { get; }
    public CanonicalObservationData Data { get; }

    public static ObservationSubmission Create(
        string requestId,
        ObservationSourceReference source,
        ObservationStructureReference structure,
        DateTimeOffset observedAt,
        string dataJson) =>
        new(requestId, source, structure, observedAt, ObservationDataCanonicalizer.ParseObject(dataJson));
}

public sealed record ObservationStructureDefinition
{
    private ObservationStructureDefinition(
        ApplicationIdentifier applicationId,
        string id,
        int version,
        string schemaProfileId,
        string normalizedSchema,
        string schemaHash,
        string description,
        ObservationStructureStatus status,
        ObservationDataClassification dataClassification)
    {
        ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
        Id = TriggerSchedulingIdentifier.Qualified(id, nameof(id));
        if (version < 1) throw Failure("INVALID_STRUCTURE_VERSION", "The structure version must be positive.");
        if (schemaProfileId != SystemJsonSchemaProfile.Version2Id)
            throw Failure("OBSERVATION_STRUCTURE_PROFILE", "Observation structures require the current system JSON Schema profile.");
        if (string.IsNullOrWhiteSpace(normalizedSchema) ||
            Encoding.UTF8.GetByteCount(normalizedSchema) > SystemJsonSchemaProfile.MaximumSchemaBytes)
            throw Failure("OBSERVATION_STRUCTURE_SCHEMA", "The normalized structure schema is invalid.");
        if (!IsObjectRootWithClosedProperties(normalizedSchema))
            throw Failure("OBSERVATION_STRUCTURE_ROOT", "Observation structures require an object root with closed top-level properties.");
        var expectedHash = TriggerSchedulingFingerprint.Sha256(Encoding.UTF8.GetBytes(normalizedSchema));
        if (!string.Equals(schemaHash, expectedHash, StringComparison.Ordinal))
            throw Failure("OBSERVATION_STRUCTURE_HASH", "The structure schema hash does not match its normalized schema.");
        if (string.IsNullOrWhiteSpace(description) || description.Length > 1024)
            throw Failure("OBSERVATION_STRUCTURE_DESCRIPTION", "The structure description is required and bounded.");
        if (!Enum.IsDefined(status))
            throw Failure("OBSERVATION_STRUCTURE_STATUS", "The structure status is invalid.");
        if (!Enum.IsDefined(dataClassification))
            throw Failure("OBSERVATION_STRUCTURE_CLASSIFICATION", "The observation data classification is invalid.");

        Version = version;
        SchemaProfileId = schemaProfileId;
        NormalizedSchema = normalizedSchema;
        SchemaHash = schemaHash;
        Description = description;
        Status = status;
        DataClassification = dataClassification;
    }

    public ApplicationIdentifier ApplicationId { get; }
    public string Id { get; }
    public int Version { get; }
    public string SchemaProfileId { get; }
    public string NormalizedSchema { get; }
    public string SchemaHash { get; }
    public string Description { get; }
    public ObservationStructureStatus Status { get; }
    public ObservationDataClassification DataClassification { get; }

    public static ObservationStructureDefinition Create(
        ApplicationIdentifier applicationId,
        string id,
        int version,
        string schemaProfileId,
        string normalizedSchema,
        string schemaHash,
        string description,
        ObservationStructureStatus status = ObservationStructureStatus.Active,
        ObservationDataClassification dataClassification = ObservationDataClassification.General) =>
        new(applicationId, id, version, schemaProfileId, normalizedSchema, schemaHash, description,
            status, dataClassification);

    private static bool IsObjectRootWithClosedProperties(string schema)
    {
        try
        {
            using var document = JsonDocument.Parse(schema, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = SystemJsonSchemaProfile.MaximumSchemaDepth
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String || type.GetString() != "object") return false;
            return document.RootElement.TryGetProperty("additionalProperties", out var additionalProperties) &&
                additionalProperties.ValueKind == JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record ObservationSourceDefinition
{
    private ObservationSourceDefinition(
        ApplicationIdentifier applicationId,
        string id,
        int version,
        ObservationSourceStatus status,
        IReadOnlyList<ObservationStructureReference> allowedStructures,
        IReadOnlyList<string> allowedPrincipalIds,
        TimeSpan replayWindow,
        int requestsPerMinute)
    {
        ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
        Id = TriggerSchedulingIdentifier.Qualified(id, nameof(id));
        if (version < 1) throw Failure("INVALID_SOURCE_VERSION", "The source version must be positive.");
        if (!Enum.IsDefined(status)) throw Failure("OBSERVATION_SOURCE_STATUS", "The source status is invalid.");
        if (allowedStructures is null || allowedStructures.Count == 0)
            throw Failure("OBSERVATION_SOURCE_STRUCTURES", "A source must allow at least one exact structure version.");
        if (allowedStructures.Any(value => value is null) ||
            allowedStructures.GroupBy(value => (value.Id, value.Version)).Any(group => group.Skip(1).Any()))
            throw Failure("OBSERVATION_SOURCE_STRUCTURES", "A source may list each structure version only once.");
        if (allowedPrincipalIds is null || allowedPrincipalIds.Count == 0 ||
            allowedPrincipalIds.Any(value => !TrustedPrincipalContext.IsValidPrincipalId(value)) ||
            allowedPrincipalIds.Distinct(StringComparer.Ordinal).Count() != allowedPrincipalIds.Count)
            throw Failure("OBSERVATION_SOURCE_PRINCIPALS", "A source must allow one or more distinct opaque principals.");
        if (replayWindow < TimeSpan.FromSeconds(1) ||
            replayWindow > TimeSpan.FromDays(TriggerSchedulingLimits.MaximumReplayWindowDays) ||
            replayWindow.Ticks % TimeSpan.TicksPerSecond != 0)
            throw Failure("OBSERVATION_SOURCE_REPLAY_WINDOW", "The source replay window must be an integral number of seconds from one second through seven days.");
        if (requestsPerMinute is < 1 or > 10)
            throw Failure("OBSERVATION_SOURCE_RATE", "The source rate must be between one and ten requests per minute.");

        Version = version;
        Status = status;
        AllowedStructures = Array.AsReadOnly(allowedStructures.ToArray());
        AllowedPrincipalIds = Array.AsReadOnly(allowedPrincipalIds.Order(StringComparer.Ordinal).ToArray());
        ReplayWindow = replayWindow;
        RequestsPerMinute = requestsPerMinute;
    }

    public ApplicationIdentifier ApplicationId { get; }
    public string Id { get; }
    public int Version { get; }
    public ObservationSourceStatus Status { get; }
    public IReadOnlyList<ObservationStructureReference> AllowedStructures { get; }
    public IReadOnlyList<string> AllowedPrincipalIds { get; }
    public TimeSpan ReplayWindow { get; }
    public int RequestsPerMinute { get; }

    public static ObservationSourceDefinition Create(
        ApplicationIdentifier applicationId,
        string id,
        int version,
        ObservationSourceStatus status,
        IReadOnlyList<ObservationStructureReference> allowedStructures,
        IReadOnlyList<string> allowedPrincipalIds,
        TimeSpan replayWindow,
        int requestsPerMinute) =>
        new(applicationId, id, version, status, allowedStructures, allowedPrincipalIds, replayWindow, requestsPerMinute);
}

public sealed record AdmittedObservation(
    ApplicationIdentifier ApplicationId,
    ObservationSubmission Submission,
    int SourceVersion,
    string StructureHash,
    string RequestFingerprint);

public interface ITriggerClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemTriggerClock : ITriggerClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class FakeTriggerClock(DateTimeOffset utcNow) : ITriggerClock
{
    private DateTimeOffset utcNow = RequireUtc(utcNow);

    public DateTimeOffset UtcNow => utcNow;

    public void Set(DateTimeOffset value) => utcNow = RequireUtc(value);

    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw Failure("TRIGGER_CLOCK_REWIND", "The fake trigger clock cannot move backwards.");
        utcNow = utcNow.Add(duration);
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
            throw Failure("TRIGGER_CLOCK_NOT_UTC", "The trigger clock must use UTC.");
        return value;
    }
}

public static class ObservationAdmissionEvaluator
{
    public static AdmittedObservation Evaluate(
        ApplicationIdentifier applicationId,
        ObservationSubmission submission,
        ObservationSourceDefinition source,
        ObservationStructureDefinition structure,
        ITriggerClock clock)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero)
            throw Failure("TRIGGER_CLOCK_NOT_UTC", "The trigger clock must use UTC.");
        if (source.ApplicationId != applicationId || structure.ApplicationId != applicationId)
            throw Failure("OBSERVATION_APPLICATION_SCOPE", "The observation source and structure must belong to the route application.");
        if (source.Status != ObservationSourceStatus.Enabled)
            throw Failure("OBSERVATION_SOURCE_DISABLED", "The observation source is disabled.");
        if (structure.Status != ObservationStructureStatus.Active)
            throw Failure("OBSERVATION_STRUCTURE_STALE", "The observation structure is not active.");
        if (!string.Equals(submission.Source.Id, source.Id, StringComparison.Ordinal))
            throw Failure("OBSERVATION_SOURCE_MISMATCH", "The submission source does not match the registered source.");
        if (!string.Equals(submission.Structure.Id, structure.Id, StringComparison.Ordinal) ||
            submission.Structure.Version != structure.Version)
            throw Failure("OBSERVATION_STRUCTURE_MISMATCH", "The submission structure does not match the registered structure.");
        if (!source.AllowedStructures.Any(value =>
            value.Id == structure.Id && value.Version == structure.Version))
            throw Failure("OBSERVATION_STRUCTURE_FORBIDDEN", "The source is not allowed to submit the requested structure version.");
        if (submission.ObservedAt > now.AddMinutes(TriggerSchedulingLimits.MaximumFutureSkewMinutes))
            throw Failure("OBSERVATION_TIME_FUTURE", "The observation time is too far in the future.");
        if (submission.ObservedAt < now - source.ReplayWindow)
            throw Failure("OBSERVATION_TIME_EXPIRED", "The observation time is outside the source replay window.");

        return new(applicationId, submission, source.Version, structure.SchemaHash,
            TriggerSchedulingFingerprint.Observation(applicationId, submission, source.Version, structure.SchemaHash));
    }
}

public sealed record OneTimeTriggerDefinition
{
    private OneTimeTriggerDefinition(
        ApplicationIdentifier applicationId,
        string id,
        int version,
        DateTimeOffset dueAt,
        TriggerMisfirePolicy misfirePolicy,
        TriggerFireTarget target,
        TriggerLifecycle lifecycle,
        TriggerNotificationTarget? notification)
    {
        ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
        Id = TriggerSchedulingIdentifier.Qualified(id, nameof(id));
        if (version < 1) throw Failure("INVALID_TRIGGER_VERSION", "The trigger version must be positive.");
        if (dueAt.Offset != TimeSpan.Zero)
            throw Failure("TRIGGER_DUE_NOT_UTC", "The trigger due time must be UTC.");
        if (!Enum.IsDefined(misfirePolicy))
            throw Failure("TRIGGER_MISFIRE_POLICY", "The trigger misfire policy is invalid.");
        if (target != TriggerFireTarget.NotificationOnly)
            throw Failure("TRIGGER_TARGET_UNSUPPORTED", "Only notification-only triggers are supported.");
        if (!Enum.IsDefined(lifecycle))
            throw Failure("TRIGGER_LIFECYCLE", "The trigger lifecycle is invalid.");
        Version = version;
        DueAt = dueAt;
        MisfirePolicy = misfirePolicy;
        Target = target;
        Lifecycle = lifecycle;
        Notification = notification ?? TriggerNotificationTarget.Default(Id);
    }

    public ApplicationIdentifier ApplicationId { get; }
    public string Id { get; }
    public int Version { get; }
    public DateTimeOffset DueAt { get; }
    public TriggerMisfirePolicy MisfirePolicy { get; }
    public TriggerFireTarget Target { get; }
    public TriggerLifecycle Lifecycle { get; }
    public TriggerNotificationTarget Notification { get; }

    public static OneTimeTriggerDefinition Create(
        ApplicationIdentifier applicationId,
        string id,
        int version,
        DateTimeOffset dueAt,
        TriggerMisfirePolicy misfirePolicy,
        TriggerFireTarget target = TriggerFireTarget.NotificationOnly,
        TriggerLifecycle lifecycle = TriggerLifecycle.Active,
        TriggerNotificationTarget? notification = null) =>
        new(applicationId, id, version, dueAt, misfirePolicy, target, lifecycle, notification);
}

public sealed record OneTimeTriggerEvaluation(
    OneTimeTriggerDisposition Disposition,
    string FireId,
    DateTimeOffset OccurrenceAt);

public static class OneTimeTriggerEvaluator
{
    public static OneTimeTriggerEvaluation Evaluate(OneTimeTriggerDefinition trigger, ITriggerClock clock)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.UtcNow;
        if (now.Offset != TimeSpan.Zero)
            throw Failure("TRIGGER_CLOCK_NOT_UTC", "The trigger clock must use UTC.");

        var disposition = trigger.Lifecycle == TriggerLifecycle.Cancelled || now < trigger.DueAt
            ? OneTimeTriggerDisposition.Pending
            : trigger.MisfirePolicy == TriggerMisfirePolicy.Skip && now > trigger.DueAt
                ? OneTimeTriggerDisposition.Missed
                : trigger.MisfirePolicy == TriggerMisfirePolicy.FireOnce &&
                    now > trigger.DueAt.AddHours(TriggerSchedulingLimits.MaximumFireOnceLatenessHours)
                    ? OneTimeTriggerDisposition.Missed
                    : OneTimeTriggerDisposition.Due;

        return new(disposition, TriggerSchedulingFingerprint.Fire(trigger), trigger.DueAt);
    }
}

public static class TriggerSchedulingFingerprint
{
    private const string ObservationDomain = "dantes-roleplay/trigger-scheduling/observation/v1\0";
    private const string FireDomain = "dantes-roleplay/trigger-scheduling/fire/v1\0";
    private const string RecurringFireDomain = "dantes-roleplay/trigger-scheduling/recurring-fire/v1\0";

    public static string Observation(
        ApplicationIdentifier applicationId,
        ObservationSubmission submission,
        int sourceVersion,
        string structureHash)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            applicationId = applicationId.Value,
            requestId = submission.RequestId,
            sourceId = submission.Source.Id,
            sourceVersion,
            instanceId = submission.Source.InstanceId,
            occurrenceId = submission.Source.OccurrenceId,
            structureId = submission.Structure.Id,
            structureVersion = submission.Structure.Version,
            structureHash,
            observedAt = submission.ObservedAt.ToString("O", CultureInfo.InvariantCulture),
            dataHash = submission.Data.Hash
        });
        return Sha256(Encoding.UTF8.GetBytes(ObservationDomain + canonical));
    }

    public static string Fire(OneTimeTriggerDefinition trigger)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            applicationId = trigger.ApplicationId.Value,
            triggerId = trigger.Id,
            triggerVersion = trigger.Version,
            occurrenceAt = trigger.DueAt.ToString("O", CultureInfo.InvariantCulture)
        });
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(FireDomain + canonical));
        return "trigger-fire." + Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    public static string RecurringFire(RecurringTriggerDefinition trigger, DateTimeOffset occurrenceAt)
    {
        if (occurrenceAt.Offset != TimeSpan.Zero)
            throw Failure("TRIGGER_CLOCK_NOT_UTC", "The trigger occurrence must use UTC.");
        var canonical = JsonSerializer.Serialize(new
        {
            applicationId = trigger.ApplicationId.Value,
            triggerId = trigger.Id,
            triggerVersion = trigger.Version,
            occurrenceAt = occurrenceAt.ToString("O", CultureInfo.InvariantCulture)
        });
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(RecurringFireDomain + canonical));
        return "trigger-fire." + Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    public static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}

internal static class TriggerSchedulingIdentifier
{
    public static string Qualified(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 3 or > 200 ||
            !System.Text.RegularExpressions.Regex.IsMatch(value,
                "^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant |
                System.Text.RegularExpressions.RegexOptions.NonBacktracking))
        {
            throw Failure("TRIGGER_SCHEDULING_ID", "The identifier must be a bounded lowercase dotted identifier.");
        }

        return value;
    }

    public static string RequestId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !System.Text.RegularExpressions.Regex.IsMatch(value,
            "^observation-request\\.[0-9a-f]{32}$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant |
            System.Text.RegularExpressions.RegexOptions.NonBacktracking))
        {
            throw Failure("OBSERVATION_REQUEST_ID", "The observation request ID is invalid.");
        }

        return value;
    }

    public static string OccurrencePart(string value, string parameter, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum ||
            !System.Text.RegularExpressions.Regex.IsMatch(value,
                "^[A-Za-z0-9][A-Za-z0-9._:-]*$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant |
                System.Text.RegularExpressions.RegexOptions.NonBacktracking))
        {
            throw Failure("OBSERVATION_OCCURRENCE_ID", "The observation instance or occurrence ID is invalid.");
        }

        return value;
    }
}

internal static class TriggerSchedulingFailures
{
    public static TriggerSchedulingContractException Failure(string code, string message) =>
        new(code, message);
}
