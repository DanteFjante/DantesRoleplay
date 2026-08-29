using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;

namespace DantesRoleplay.TriggerScheduling;

public static class TriggerSchedulingAdministrationVocabulary
{
    public static readonly IReadOnlyList<string> Operations = Array.AsReadOnly(new[]
    {
        "structure.register", "source.register", "one-time.register", "recurring.register",
        "conditional.register", "observation-trigger.register", "phone.register", "phone.revoke"
    });

    public static readonly IReadOnlyList<string> Resources = Array.AsReadOnly(new[]
    {
        "overview", "structures", "sources", "devices", "one-time", "recurring", "conditional",
        "observation-triggers", "observations", "fires", "phone-principal"
    });
}

public sealed record TriggerSchedulingAdministrationCommand
{
    private TriggerSchedulingAdministrationCommand(string requestToken, string operation,
        ApplicationIdentifier applicationId, CanonicalObservationData value)
    {
        if (requestToken is not { Length: 32 } || requestToken.Any(character =>
                !(char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')))
            throw Failure("TRIGGER_ADMIN_TOKEN", "requestToken must contain exactly 32 lowercase hexadecimal characters.");
        var normalizedOperation = operation?.Trim().ToLowerInvariant();
        if (normalizedOperation is null ||
            !TriggerSchedulingAdministrationVocabulary.Operations.Contains(normalizedOperation, StringComparer.Ordinal))
            throw Failure("TRIGGER_ADMIN_OPERATION", "The trigger administration operation is unsupported.");
        RequestToken = requestToken;
        Operation = normalizedOperation;
        ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string RequestToken { get; }
    public string Operation { get; }
    public ApplicationIdentifier ApplicationId { get; }
    public CanonicalObservationData Value { get; }

    public static TriggerSchedulingAdministrationCommand Create(string requestToken, string operation,
        ApplicationIdentifier applicationId, string valueJson) =>
        new(requestToken, operation, applicationId, ObservationDataCanonicalizer.ParseObject(valueJson));

    public static TriggerSchedulingAdministrationCommand Parse(string json)
    {
        CanonicalObservationData outer;
        try { outer = ObservationDataCanonicalizer.ParseObject(json); }
        catch (TriggerSchedulingContractException exception)
        { throw new TriggerSchedulingAdministrationException("TRIGGER_ADMIN_PAYLOAD", exception.Message); }
        using var document = JsonDocument.Parse(outer.Json);
        var root = document.RootElement;
        var required = new[] { "requestToken", "operation", "applicationId", "value" };
        if (!root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
                .SetEquals(required) || root.EnumerateObject().Count() != required.Length)
            throw new TriggerSchedulingAdministrationException("TRIGGER_ADMIN_PAYLOAD",
                "The command must contain exactly requestToken, operation, applicationId, and value.");
        if (root.GetProperty("requestToken").ValueKind != JsonValueKind.String ||
            root.GetProperty("operation").ValueKind != JsonValueKind.String ||
            root.GetProperty("applicationId").ValueKind != JsonValueKind.String ||
            root.GetProperty("value").ValueKind != JsonValueKind.Object)
            throw new TriggerSchedulingAdministrationException("TRIGGER_ADMIN_PAYLOAD",
                "The command fields have invalid JSON types.");
        try
        {
            return Create(root.GetProperty("requestToken").GetString() ?? string.Empty,
                root.GetProperty("operation").GetString() ?? string.Empty,
                ApplicationIdentifier.Parse(root.GetProperty("applicationId").GetString() ?? string.Empty),
                root.GetProperty("value").GetRawText());
        }
        catch (ArgumentException exception)
        { throw new TriggerSchedulingAdministrationException("TRIGGER_ADMIN_PAYLOAD", exception.Message); }
    }

    private static TriggerSchedulingAdministrationException Failure(string code, string message) => new(code, message);
}

public sealed record TriggerSchedulingAdministrationContext(
    string Intent,
    IReadOnlyList<string> ProceduresUsed,
    AuthorizationAuditEvidence AuthorizationEvidence);

public sealed record TriggerSchedulingAdministrationQuery
{
    private TriggerSchedulingAdministrationQuery(ApplicationIdentifier? applicationId, string resource,
        string? id, int limit)
    {
        var normalized = string.IsNullOrWhiteSpace(resource) ? "overview" : resource.Trim().ToLowerInvariant();
        if (!TriggerSchedulingAdministrationVocabulary.Resources.Contains(normalized, StringComparer.Ordinal))
            throw new TriggerSchedulingAdministrationException("TRIGGER_ADMIN_RESOURCE",
                "The trigger administration resource is unsupported.");
        if (applicationId is null && (normalized != "overview" || id is not null))
            throw new TriggerSchedulingAdministrationException("TRIGGER_ADMIN_APPLICATION_REQUIRED",
                "An application is required for this trigger administration query.");
        if (id is { Length: > 200 } || id?.Any(char.IsControl) == true)
            throw new TriggerSchedulingAdministrationException("TRIGGER_ADMIN_ID", "The query ID is invalid.");
        ApplicationId = applicationId;
        Resource = normalized;
        Id = string.IsNullOrWhiteSpace(id) ? null : id;
        Limit = limit <= 0 ? 50 : Math.Min(limit, 100);
    }

    public ApplicationIdentifier? ApplicationId { get; }
    public string Resource { get; }
    public string? Id { get; }
    public int Limit { get; }

    public static TriggerSchedulingAdministrationQuery Create(ApplicationIdentifier? applicationId,
        string? resource = null, string? id = null, int limit = 50) =>
        new(applicationId, resource ?? "overview", id, limit);
}

public sealed record TriggerSchedulingApplicationSummary(
    ApplicationIdentifier ApplicationId,
    string DisplayName,
    int Structures,
    int Sources,
    int Devices,
    int OneTimeTriggers,
    int RecurringTriggers,
    int ConditionalTriggers,
    int ObservationTriggers,
    int Observations);

public sealed record TriggerObservationAdministrationView(
    string Id,
    ApplicationIdentifier ApplicationId,
    string SourceId,
    int SourceVersion,
    string SourceInstanceId,
    string OccurrenceId,
    string StructureId,
    int StructureVersion,
    string StructureHash,
    DateTimeOffset ObservedAt,
    DateTimeOffset ReceivedAt,
    string DataHash,
    string? PrincipalId);

public sealed record TriggerFireAdministrationView(
    string Kind,
    string Id,
    ApplicationIdentifier ApplicationId,
    string TriggerId,
    int TriggerVersion,
    DateTimeOffset OccurrenceAt,
    string Disposition,
    string? NotificationId,
    DateTimeOffset RecordedAt);

public sealed record PhoneCompanionPrincipalView(
    ApplicationIdentifier ApplicationId,
    string DeviceId,
    string PrincipalId);

public sealed record TriggerSchedulingAdministrationView(
    string Resource,
    IReadOnlyList<TriggerSchedulingApplicationSummary> Applications,
    IReadOnlyList<StoredObservationStructure> Structures,
    IReadOnlyList<StoredObservationSource> Sources,
    IReadOnlyList<PhoneCompanionDeviceView> Devices,
    IReadOnlyList<TriggerScheduleStatusView> OneTimeTriggers,
    IReadOnlyList<RecurringTriggerStatusView> RecurringTriggers,
    IReadOnlyList<ConditionalTriggerStatusView> ConditionalTriggers,
    IReadOnlyList<ObservationTriggerStatusView> ObservationTriggers,
    IReadOnlyList<TriggerObservationAdministrationView> Observations,
    IReadOnlyList<TriggerFireAdministrationView> Fires,
    PhoneCompanionPrincipalView? PhonePrincipal);

public sealed record TriggerSchedulingAdministrationResult(
    string Operation,
    ApplicationIdentifier ApplicationId,
    string Outcome,
    string OperationId,
    JsonElement Value,
    string? Credential = null);

public sealed class TriggerSchedulingAdministrationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface ITriggerSchedulingAdministrationService
{
    Task<TriggerSchedulingAdministrationView> QueryAsync(TriggerSchedulingAdministrationQuery query,
        CancellationToken cancellationToken = default);

    Task<TriggerSchedulingAdministrationResult> PreviewAsync(TriggerSchedulingAdministrationCommand command,
        TriggerSchedulingAdministrationContext context, CancellationToken cancellationToken = default);

    Task<TriggerSchedulingAdministrationResult> CommitAsync(TriggerSchedulingAdministrationCommand command,
        TriggerSchedulingAdministrationContext context, CancellationToken cancellationToken = default);
}
