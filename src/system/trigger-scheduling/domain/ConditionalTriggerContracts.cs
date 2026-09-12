using System.Globalization;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using static DantesRoleplay.TriggerScheduling.ConditionalTriggerContractFailure;

namespace DantesRoleplay.TriggerScheduling;

public enum ConditionalTriggerKind { WorldClockThreshold, StateCondition }
public enum ConditionalTriggerLifecycle { Active, Paused, Cancelled }
public enum ConditionalTriggerActivation { RisingEdge, Level }
public enum ConditionalTriggerRearm { OnFalse, Manual }
public enum ConditionalTriggerCoalescing { PerOperation }

public sealed record ConditionalTriggerAdapterReference
{
    private ConditionalTriggerAdapterReference(string id, int version)
    {
        Id = TriggerSchedulingIdentifier.Qualified(id, nameof(id));
        if (version < 1)
            throw Failure("CONDITIONAL_ADAPTER_VERSION", "The adapter version must be positive.");
        Version = version;
    }

    public string Id { get; }
    public int Version { get; }
    public static ConditionalTriggerAdapterReference Create(string id, int version) => new(id, version);
}

public sealed record ConditionalTriggerDependency
{
    private ConditionalTriggerDependency(string entityId, EcsComponentReference componentType)
    {
        if (string.IsNullOrWhiteSpace(entityId) || entityId.Length > 200)
            throw Failure("CONDITIONAL_DEPENDENCY_ENTITY", "A bounded dependency entity ID is required.");
        ArgumentNullException.ThrowIfNull(componentType);
        try { componentType.Validate(); }
        catch (ArgumentException exception)
        {
            throw Failure("CONDITIONAL_DEPENDENCY_TYPE", exception.Message);
        }
        EntityId = entityId;
        ComponentType = componentType;
    }

    public string EntityId { get; }
    public EcsComponentReference ComponentType { get; }
    public static ConditionalTriggerDependency Create(string entityId, EcsComponentReference componentType) =>
        new(entityId, componentType);
}

public sealed record ConditionalTriggerRelationshipDependency
{
    private ConditionalTriggerRelationshipDependency(string qualifiedKind, string anchorEntityId, bool incoming)
    {
        QualifiedKind = TriggerSchedulingIdentifier.Qualified(qualifiedKind, nameof(qualifiedKind));
        if (string.IsNullOrWhiteSpace(anchorEntityId) || anchorEntityId.Length > 200 ||
            anchorEntityId.Any(char.IsControl))
            throw Failure("CONDITIONAL_RELATIONSHIP_ENTITY",
                "A bounded relationship anchor entity ID is required.");
        AnchorEntityId = anchorEntityId;
        Incoming = incoming;
    }

    public string QualifiedKind { get; }
    public string AnchorEntityId { get; }
    public bool Incoming { get; }
    public static ConditionalTriggerRelationshipDependency Create(
        string qualifiedKind, string anchorEntityId, bool incoming = false) =>
        new(qualifiedKind, anchorEntityId, incoming);
}

public sealed record ConditionalTriggerObserverPredicate
{
    public const string StableAdapterId = "system.trigger.application-mechanic";
    public const int StableAdapterVersion = 1;
    private ConditionalTriggerObserverPredicate(ApplicationObserverPredicateSelection selection,
        int maximumOperationsPerFire, ConditionalTriggerCoalescing coalescing)
    {
        Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        if (maximumOperationsPerFire is < 1 or > 16)
            throw Failure("CONDITIONAL_CAUSAL_BUDGET",
                "An observer must reserve from one through sixteen operations per fire.");
        if (coalescing != ConditionalTriggerCoalescing.PerOperation)
            throw Failure("CONDITIONAL_COALESCING", "The observer coalescing policy is unsupported.");
        MaximumOperationsPerFire = maximumOperationsPerFire;
        Coalescing = coalescing;
    }

    public ApplicationObserverPredicateSelection Selection { get; }
    public int MaximumOperationsPerFire { get; }
    public ConditionalTriggerCoalescing Coalescing { get; }

    public static ConditionalTriggerObserverPredicate Create(
        ApplicationObserverPredicateSelection selection,
        int maximumOperationsPerFire,
        ConditionalTriggerCoalescing coalescing = ConditionalTriggerCoalescing.PerOperation) =>
        new(selection, maximumOperationsPerFire, coalescing);
}

public sealed record ConditionalTriggerDefinition
{
    private ConditionalTriggerDefinition(
        ApplicationIdentifier applicationId,
        string id,
        int version,
        ConditionalTriggerLifecycle lifecycle,
        ConditionalTriggerKind kind,
        ConditionalTriggerActivation activation,
        ConditionalTriggerRearm rearm,
        string stateSpaceId,
        IReadOnlyList<ConditionalTriggerDependency> dependencies,
        ConditionalTriggerAdapterReference adapter,
        CanonicalObservationData adapterConfiguration,
        TriggerFireTarget target,
        TriggerNotificationTarget notification,
        IReadOnlyList<ConditionalTriggerRelationshipDependency>? relationshipDependencies,
        ConditionalTriggerObserverPredicate? predicate,
        TriggerProcedureWorkflowTarget? procedureWorkflow,
        bool requireWorkflowPayload = true)
    {
        ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
        Id = TriggerSchedulingIdentifier.Qualified(id, nameof(id));
        if (version < 1) throw Failure("INVALID_TRIGGER_VERSION", "The trigger version must be positive.");
        Version = version;
        if (!Enum.IsDefined(lifecycle) || !Enum.IsDefined(kind) || !Enum.IsDefined(activation) || !Enum.IsDefined(rearm))
            throw Failure("CONDITIONAL_POLICY", "The conditional trigger policy is invalid.");
        if (string.IsNullOrWhiteSpace(stateSpaceId) || stateSpaceId.Length > 200)
            throw Failure("CONDITIONAL_STATE_SPACE", "A bounded state-space ID is required.");
        ArgumentNullException.ThrowIfNull(dependencies);
        var relationships = relationshipDependencies?.ToArray() ?? [];
        if (dependencies.Count + relationships.Length is < 1 or > 16 ||
            dependencies.Any(value => value is null) || relationships.Any(value => value is null))
            throw Failure("CONDITIONAL_DEPENDENCY_COUNT",
                "A condition requires 1 to 16 exact component or relationship dependencies.");
        if (dependencies.Select(value => (value.EntityId, value.ComponentType.QualifiedTypeId))
            .Distinct().Count() != dependencies.Count)
            throw Failure("CONDITIONAL_DEPENDENCY_DUPLICATE", "Condition dependencies must be distinct.");
        if (relationships.Select(value => (value.QualifiedKind, value.AnchorEntityId, value.Incoming))
            .Distinct().Count() != relationships.Length)
            throw Failure("CONDITIONAL_DEPENDENCY_DUPLICATE", "Condition dependencies must be distinct.");
        Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        AdapterConfiguration = adapterConfiguration ?? throw new ArgumentNullException(nameof(adapterConfiguration));
        if (!Enum.IsDefined(target) ||
            (target == TriggerFireTarget.NotificationOnly && (predicate is not null || procedureWorkflow is not null)) ||
            (target == TriggerFireTarget.ProcedureWorkflow &&
             (predicate is null || requireWorkflowPayload && procedureWorkflow is null)))
            throw Failure("CONDITIONAL_TARGET", "The conditional trigger target does not match its closed payload shape.");
        if (predicate is not null && predicate.Selection.ApplicationId != applicationId)
            throw Failure("CONDITIONAL_PREDICATE_APPLICATION",
                "The observer predicate must belong to the trigger application.");
        if (procedureWorkflow is not null &&
            (procedureWorkflow.InvocationHost.ApplicationRevision.ApplicationId != applicationId ||
             procedureWorkflow.InvocationHost.StateSpaceId != stateSpaceId))
            throw Failure("CONDITIONAL_WORKFLOW_SCOPE",
                "The observer workflow must use the trigger application and state space.");
        Notification = notification ?? throw new ArgumentNullException(nameof(notification));
        if (kind == ConditionalTriggerKind.WorldClockThreshold &&
            (activation != ConditionalTriggerActivation.RisingEdge || rearm != ConditionalTriggerRearm.Manual))
            throw Failure("WORLD_CLOCK_TRIGGER_POLICY",
                "World-clock thresholds require rising-edge activation and manual re-arm.");

        Lifecycle = lifecycle;
        Kind = kind;
        Activation = activation;
        Rearm = rearm;
        StateSpaceId = stateSpaceId;
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
        RelationshipDependencies = Array.AsReadOnly(relationships);
        Target = target;
        Predicate = predicate;
        ProcedureWorkflow = procedureWorkflow;
    }

    public ApplicationIdentifier ApplicationId { get; }
    public string Id { get; }
    public int Version { get; }
    public ConditionalTriggerLifecycle Lifecycle { get; }
    public ConditionalTriggerKind Kind { get; }
    public ConditionalTriggerActivation Activation { get; }
    public ConditionalTriggerRearm Rearm { get; }
    public string StateSpaceId { get; }
    public IReadOnlyList<ConditionalTriggerDependency> Dependencies { get; }
    public IReadOnlyList<ConditionalTriggerRelationshipDependency> RelationshipDependencies { get; }
    public ConditionalTriggerAdapterReference Adapter { get; }
    public CanonicalObservationData AdapterConfiguration { get; }
    public TriggerFireTarget Target { get; }
    public TriggerNotificationTarget Notification { get; }
    public ConditionalTriggerObserverPredicate? Predicate { get; }
    public TriggerProcedureWorkflowTarget? ProcedureWorkflow { get; }

    public static ConditionalTriggerDefinition Create(
        ApplicationIdentifier applicationId,
        string id,
        int version,
        ConditionalTriggerLifecycle lifecycle,
        ConditionalTriggerKind kind,
        ConditionalTriggerActivation activation,
        ConditionalTriggerRearm rearm,
        string stateSpaceId,
        IReadOnlyList<ConditionalTriggerDependency> dependencies,
        ConditionalTriggerAdapterReference adapter,
        string adapterConfigurationJson,
        TriggerFireTarget target,
        TriggerNotificationTarget notification,
        IReadOnlyList<ConditionalTriggerRelationshipDependency>? relationshipDependencies = null,
        ConditionalTriggerObserverPredicate? predicate = null,
        TriggerProcedureWorkflowTarget? procedureWorkflow = null) =>
        new(applicationId, id, version, lifecycle, kind, activation, rearm, stateSpaceId,
            dependencies, adapter, ObservationDataCanonicalizer.ParseObject(adapterConfigurationJson), target, notification,
            relationshipDependencies, predicate, procedureWorkflow);

    public static ConditionalTriggerDefinition Stored(
        ApplicationIdentifier applicationId, string id, int version,
        ConditionalTriggerLifecycle lifecycle, ConditionalTriggerKind kind,
        ConditionalTriggerActivation activation, ConditionalTriggerRearm rearm,
        string stateSpaceId, IReadOnlyList<ConditionalTriggerDependency> dependencies,
        ConditionalTriggerAdapterReference adapter, string adapterConfigurationJson,
        TriggerFireTarget target, TriggerNotificationTarget notification,
        IReadOnlyList<ConditionalTriggerRelationshipDependency>? relationshipDependencies = null,
        ConditionalTriggerObserverPredicate? predicate = null) =>
        new(applicationId, id, version, lifecycle, kind, activation, rearm, stateSpaceId,
            dependencies, adapter, ObservationDataCanonicalizer.ParseObject(adapterConfigurationJson), target, notification,
            relationshipDependencies, predicate, null, requireWorkflowPayload: false);
}

public sealed record ConditionalTriggerDependencySnapshot(
    ConditionalTriggerDependency Dependency,
    string? ValueJson,
    int? Revision)
{
    public bool Present => ValueJson is not null;
}

/// <summary>A reviewed coded adapter; runtime definitions can select it but cannot upload code.</summary>
public interface IConditionalTriggerAdapter
{
    string Id { get; }
    int Version { get; }
    void Validate(ConditionalTriggerDefinition definition);
    bool Evaluate(
        ConditionalTriggerDefinition definition,
        IReadOnlyList<ConditionalTriggerDependencySnapshot> dependencies);
}

public sealed class ClosedScalarConditionalTriggerAdapter : IConditionalTriggerAdapter
{
    public const string StableId = "system.trigger.closed-scalar";
    public const int StableVersion = 1;
    private static readonly string[] Operators = ["eq", "ne", "gt", "gte", "lt", "lte"];

    public string Id => StableId;
    public int Version => StableVersion;

    public void Validate(ConditionalTriggerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Dependencies.Count != 1)
            throw Failure("CLOSED_SCALAR_DEPENDENCY", "The closed-scalar adapter requires exactly one dependency.");
        var config = Parse(definition.AdapterConfiguration.Json);
        if (definition.Kind == ConditionalTriggerKind.WorldClockThreshold &&
            (config.Operator != "gte" || config.Value.ValueKind != JsonValueKind.Number))
            throw Failure("WORLD_CLOCK_TRIGGER_COMPARISON",
                "World-clock thresholds require one numeric greater-than-or-equal comparison.");
    }

    public bool Evaluate(
        ConditionalTriggerDefinition definition,
        IReadOnlyList<ConditionalTriggerDependencySnapshot> dependencies)
    {
        Validate(definition);
        ArgumentNullException.ThrowIfNull(dependencies);
        if (dependencies.Count != 1 || !dependencies[0].Present) return false;
        JsonDocument valueDocument;
        try { valueDocument = JsonDocument.Parse(dependencies[0].ValueJson!); }
        catch (JsonException) { throw Failure("CONDITIONAL_COMPONENT_JSON", "A dependency contains invalid JSON."); }
        using (valueDocument)
        {
            if (valueDocument.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (valueDocument.RootElement.EnumerateObject().GroupBy(value => value.Name, StringComparer.Ordinal)
                .Any(group => group.Skip(1).Any()))
                throw Failure("CONDITIONAL_COMPONENT_DUPLICATE_PROPERTY",
                    "A dependency component must not contain duplicate top-level properties.");
            var config = Parse(definition.AdapterConfiguration.Json);
            if (config.GuardProperty is not null &&
                (!valueDocument.RootElement.TryGetProperty(config.GuardProperty, out var actualGuard) ||
                 !Equal(actualGuard, config.GuardValue!.Value)))
                return false;
            if (!valueDocument.RootElement.TryGetProperty(config.Property, out var actual)) return false;
            return Compare(actual, config.Value, config.Operator);
        }
    }

    private static ScalarConfig Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var allowed = new HashSet<string>(["property", "operator", "value", "guardProperty", "guardValue"], StringComparer.Ordinal);
        if (root.EnumerateObject().Any(value => !allowed.Contains(value.Name)))
            throw Failure("CLOSED_SCALAR_CONFIG_FIELD", "The closed-scalar configuration contains an unknown field.");
        if (!root.TryGetProperty("property", out var propertyElement) || propertyElement.ValueKind != JsonValueKind.String ||
            !ValidProperty(propertyElement.GetString()))
            throw Failure("CLOSED_SCALAR_PROPERTY", "A simple bounded top-level property name is required.");
        if (!root.TryGetProperty("operator", out var operatorElement) || operatorElement.ValueKind != JsonValueKind.String ||
            !Operators.Contains(operatorElement.GetString(), StringComparer.Ordinal))
            throw Failure("CLOSED_SCALAR_OPERATOR", "The scalar comparison operator is invalid.");
        if (!root.TryGetProperty("value", out var value) || !Scalar(value))
            throw Failure("CLOSED_SCALAR_VALUE", "The comparison value must be a JSON scalar.");
        var hasGuardProperty = root.TryGetProperty("guardProperty", out var guardPropertyElement);
        var hasGuardValue = root.TryGetProperty("guardValue", out var guardValueElement);
        if (hasGuardProperty != hasGuardValue || hasGuardProperty &&
            (guardPropertyElement.ValueKind != JsonValueKind.String || !ValidProperty(guardPropertyElement.GetString()) ||
             !Scalar(guardValueElement)))
            throw Failure("CLOSED_SCALAR_GUARD", "A guard requires one top-level property and scalar value.");
        var operation = operatorElement.GetString()!;
        if (operation is "gt" or "gte" or "lt" or "lte" && value.ValueKind != JsonValueKind.Number)
            throw Failure("CLOSED_SCALAR_ORDER_VALUE", "Ordered comparisons require a numeric value.");
        return new(propertyElement.GetString()!, operation, value.Clone(),
            hasGuardProperty ? guardPropertyElement.GetString() : null,
            hasGuardValue ? guardValueElement.Clone() : null);
    }

    private static bool Compare(JsonElement actual, JsonElement expected, string operation)
    {
        if (operation == "eq") return Equal(actual, expected);
        if (operation == "ne") return !Equal(actual, expected);
        if (actual.ValueKind != JsonValueKind.Number || expected.ValueKind != JsonValueKind.Number ||
            !decimal.TryParse(actual.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var left) ||
            !decimal.TryParse(expected.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var right))
            return false;
        return operation switch
        {
            "gt" => left > right,
            "gte" => left >= right,
            "lt" => left < right,
            "lte" => left <= right,
            _ => false
        };
    }

    private static bool Equal(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        return left.ValueKind switch
        {
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => decimal.TryParse(left.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
                decimal.TryParse(right.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && a == b,
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null => true,
            _ => false
        };
    }

    private static bool Scalar(JsonElement value) => value.ValueKind is JsonValueKind.String or
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;

    private static bool ValidProperty(string? value) => value is { Length: >= 1 and <= 100 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private sealed record ScalarConfig(string Property, string Operator, JsonElement Value,
        string? GuardProperty, JsonElement? GuardValue);
}

internal static class ConditionalTriggerContractFailure
{
    internal static TriggerSchedulingContractException Failure(string code, string message) => new(code, message);
}
