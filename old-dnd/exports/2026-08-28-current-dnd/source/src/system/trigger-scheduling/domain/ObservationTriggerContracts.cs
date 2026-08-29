using System.Globalization;
using System.Text.Json;
using DantesRoleplay.Applications;
using static DantesRoleplay.TriggerScheduling.ObservationTriggerContractFailure;

namespace DantesRoleplay.TriggerScheduling;

public enum ObservationTriggerLifecycle { Active, Paused, Cancelled }

public sealed record ObservationMatchAdapterReference
{
    private ObservationMatchAdapterReference(string id, int version)
    {
        Id = TriggerSchedulingIdentifier.Qualified(id, nameof(id));
        if (version < 1) throw Failure("OBSERVATION_MATCH_ADAPTER_VERSION",
            "The observation matcher version must be positive.");
        Version = version;
    }
    public string Id { get; }
    public int Version { get; }
    public static ObservationMatchAdapterReference Create(string id, int version) => new(id, version);
}

public sealed record ObservationTriggerDefinition
{
    private ObservationTriggerDefinition(ApplicationIdentifier applicationId, string id, int version,
        ObservationTriggerLifecycle lifecycle, string sourceId, int sourceVersion, string structureId,
        int structureVersion, string structureHash, ObservationMatchAdapterReference adapter,
        CanonicalObservationData adapterConfiguration, TriggerFireTarget target,
        TriggerNotificationTarget notification)
    {
        ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
        Id = TriggerSchedulingIdentifier.Qualified(id, nameof(id));
        if (version < 1) throw Failure("INVALID_TRIGGER_VERSION", "The trigger version must be positive.");
        if (!Enum.IsDefined(lifecycle)) throw Failure("OBSERVATION_TRIGGER_LIFECYCLE",
            "The observation trigger lifecycle is invalid.");
        SourceId = TriggerSchedulingIdentifier.Qualified(sourceId, nameof(sourceId));
        if (sourceVersion < 1) throw Failure("INVALID_SOURCE_VERSION", "The source version must be positive.");
        StructureId = TriggerSchedulingIdentifier.Qualified(structureId, nameof(structureId));
        if (structureVersion < 1) throw Failure("INVALID_STRUCTURE_VERSION", "The structure version must be positive.");
        if (structureHash is not { Length: 64 } ||
            !structureHash.All(value => char.IsAsciiDigit(value) || value is >= 'A' and <= 'F'))
            throw Failure("OBSERVATION_TRIGGER_STRUCTURE_HASH", "An exact uppercase structure hash is required.");
        if (target != TriggerFireTarget.NotificationOnly) throw Failure("OBSERVATION_TRIGGER_TARGET",
            "Observation triggers currently support notification-only targets.");
        Version = version;
        Lifecycle = lifecycle;
        SourceVersion = sourceVersion;
        StructureVersion = structureVersion;
        StructureHash = structureHash;
        Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        AdapterConfiguration = adapterConfiguration ?? throw new ArgumentNullException(nameof(adapterConfiguration));
        Target = target;
        Notification = notification ?? throw new ArgumentNullException(nameof(notification));
    }

    public ApplicationIdentifier ApplicationId { get; }
    public string Id { get; }
    public int Version { get; }
    public ObservationTriggerLifecycle Lifecycle { get; }
    public string SourceId { get; }
    public int SourceVersion { get; }
    public string StructureId { get; }
    public int StructureVersion { get; }
    public string StructureHash { get; }
    public ObservationMatchAdapterReference Adapter { get; }
    public CanonicalObservationData AdapterConfiguration { get; }
    public TriggerFireTarget Target { get; }
    public TriggerNotificationTarget Notification { get; }

    public static ObservationTriggerDefinition Create(ApplicationIdentifier applicationId, string id,
        int version, ObservationTriggerLifecycle lifecycle, string sourceId, int sourceVersion,
        string structureId, int structureVersion, string structureHash,
        ObservationMatchAdapterReference adapter, string adapterConfigurationJson,
        TriggerFireTarget target, TriggerNotificationTarget notification) =>
        new(applicationId, id, version, lifecycle, sourceId, sourceVersion, structureId,
            structureVersion, structureHash, adapter,
            ObservationDataCanonicalizer.ParseObject(adapterConfigurationJson), target, notification);
}

public sealed record ObservationMatchInput(
    ApplicationIdentifier ApplicationId,
    string ObservationId,
    string SourceId,
    int SourceVersion,
    string StructureId,
    int StructureVersion,
    string StructureHash,
    CanonicalObservationData Data);

/// <summary>Reviewed startup code. Definitions select an exact registered revision; no code is uploaded.</summary>
public interface IObservationMatchAdapter
{
    string Id { get; }
    int Version { get; }
    void Validate(ObservationTriggerDefinition definition);
    bool Evaluate(ObservationTriggerDefinition definition, ObservationMatchInput observation);
}

public sealed class ClosedScalarsObservationMatchAdapter : IObservationMatchAdapter
{
    public const string StableId = "system.trigger.observation.closed-scalars";
    public const int StableVersion = 1;
    public string Id => StableId;
    public int Version => StableVersion;

    public void Validate(ObservationTriggerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Parse(definition.AdapterConfiguration.Json);
    }

    public bool Evaluate(ObservationTriggerDefinition definition, ObservationMatchInput observation)
    {
        Validate(definition);
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.ApplicationId != definition.ApplicationId ||
            observation.SourceId != definition.SourceId || observation.SourceVersion != definition.SourceVersion ||
            observation.StructureId != definition.StructureId || observation.StructureVersion != definition.StructureVersion ||
            observation.StructureHash != definition.StructureHash)
            return false;
        using var data = JsonDocument.Parse(observation.Data.Json);
        if (data.RootElement.ValueKind != JsonValueKind.Object) return false;
        foreach (var expected in Parse(definition.AdapterConfiguration.Json))
            if (!data.RootElement.TryGetProperty(expected.Property, out var actual) ||
                !Equal(actual, expected.Value)) return false;
        return true;
    }

    private static IReadOnlyList<ExpectedScalar> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var rootProperties = root.EnumerateObject().ToArray();
        if (rootProperties.Length != 1 || rootProperties[0].Name != "matches" ||
            rootProperties[0].Value.ValueKind != JsonValueKind.Array)
            throw Failure("OBSERVATION_MATCH_CONFIG_SHAPE",
                "Matcher configuration requires only a matches array.");
        var items = rootProperties[0].Value.EnumerateArray().ToArray();
        if (items.Length is < 1 or > 16)
            throw Failure("OBSERVATION_MATCH_CONFIG_COUNT", "A matcher requires 1 to 16 exact scalar fields.");
        var result = new List<ExpectedScalar>(items.Length);
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.Object) throw Failure("OBSERVATION_MATCH_CONFIG_ITEM",
                "Each matcher item must be a closed property/value object.");
            var properties = item.EnumerateObject().ToArray();
            if (properties.Length != 2 || properties.Select(value => value.Name).ToHashSet(StringComparer.Ordinal)
                .SetEquals(["property", "value"]) is false ||
                !item.TryGetProperty("property", out var property) || property.ValueKind != JsonValueKind.String ||
                !ValidProperty(property.GetString()) || !item.TryGetProperty("value", out var value) || !Scalar(value))
                throw Failure("OBSERVATION_MATCH_CONFIG_ITEM",
                    "Each matcher item requires one simple property name and JSON scalar value.");
            result.Add(new(property.GetString()!, value.Clone()));
        }
        if (result.Select(value => value.Property).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw Failure("OBSERVATION_MATCH_CONFIG_DUPLICATE", "Each matched property may be declared only once.");
        return result;
    }

    private static bool Equal(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        return left.ValueKind switch
        {
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => decimal.TryParse(left.GetRawText(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var a) && decimal.TryParse(right.GetRawText(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && a == b,
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null => true,
            _ => false
        };
    }
    private static bool Scalar(JsonElement value) => value.ValueKind is JsonValueKind.String or
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;
    private static bool ValidProperty(string? value) => value is { Length: >= 1 and <= 100 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    private sealed record ExpectedScalar(string Property, JsonElement Value);
}

internal static class ObservationTriggerContractFailure
{
    internal static TriggerSchedulingContractException Failure(string code, string message) => new(code, message);
}
