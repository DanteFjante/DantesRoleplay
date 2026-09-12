using System.Text.Json;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.SystemTasks.Persistence;

/// <summary>
/// Owner-local admission target for the schedule/observer transaction participants. Scope and grants
/// come from a freshly authorized host; the selected definition is pinned before enqueue. A binding
/// replacement affects future occurrences only.
/// </summary>
internal sealed class SystemTaskDurableTriggerTarget
{
    internal SystemTaskDurableTriggerTarget(string bindingId, int bindingVersion, string bindingFingerprint,
        string occurrenceId, InteractionInvocationHost host, SystemTaskSelectedDefinition selectedDefinition,
        string inputJson, string resultSchemaJson, SystemTaskCheckpoint? checkpoint = null)
    {
        BindingId = Identifier(bindingId, nameof(bindingId));
        if (bindingVersion < 1) throw new ArgumentOutOfRangeException(nameof(bindingVersion));
        BindingVersion = bindingVersion;
        BindingFingerprint = UpperSha256(bindingFingerprint, nameof(bindingFingerprint));
        OccurrenceId = Identifier(occurrenceId, nameof(occurrenceId));
        ArgumentNullException.ThrowIfNull(host);
        if (host.StateSpaceId is not { } stateSpaceId || host.StateRevision is null)
            throw new InteractionContractException("INVOCATION_STATE_SCOPE_REQUIRED",
                "A durable trigger requires a state scope.");
        if (host.Profile != InteractionExecutionProfile.Workflow)
            throw new InteractionContractException("SYSTEM_TASK_PROFILE_UNSUPPORTED", "A durable trigger requires the workflow profile.");
        var identity = Identity(bindingId, bindingVersion, occurrenceId, host.Principal.PrincipalId,
            host.ApplicationRevision.ApplicationId.Value, stateSpaceId);
        if (host.CommandId != identity.CommandId)
            throw new InteractionContractException("SYSTEM_TASK_TRIGGER_IDENTITY_MISMATCH", "The host command must identify the durable occurrence.");
        CorrelationId = identity.CorrelationId;
        Submission = new(host, selectedDefinition, inputJson, checkpoint);
        ResultSchemaJson = InteractionCanonicalJson.CanonicalizeObject(resultSchemaJson);
    }

    internal string BindingId { get; }
    internal int BindingVersion { get; }
    internal string BindingFingerprint { get; }
    internal string OccurrenceId { get; }
    internal string CorrelationId { get; }
    internal SystemTaskDurableSubmissionRequest Submission { get; }
    internal string ResultSchemaJson { get; }

    // Deliberately exclude definition, payload, grant, delivery time and attempt from identity:
    // a changed payload for an already delivered occurrence must conflict, never become new work.
    // The event owner supplies its durable event ID; the schedule owner supplies its stored due
    // occurrence ID, not the worker's wall clock. Existing-only binding versions are mandatory.
    internal static (string CommandId, string CorrelationId) Identity(string bindingId, int bindingVersion,
        string occurrenceId, string principalReference, string applicationId, string stateSpaceId)
    {
        if (bindingVersion < 1) throw new ArgumentOutOfRangeException(nameof(bindingVersion));
        var json = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            bindingId = Identifier(bindingId, nameof(bindingId)), bindingVersion,
            occurrenceId = Identifier(occurrenceId, nameof(occurrenceId)),
            principalReference = Identifier(principalReference, nameof(principalReference)),
            applicationId = Identifier(applicationId, nameof(applicationId)),
            stateSpaceId = Identifier(stateSpaceId, nameof(stateSpaceId))
        }));
        var hash = InteractionCanonicalJson.Fingerprint("system-task/durable-trigger-occurrence/v1", json).ToLowerInvariant();
        return ("trigger." + hash, "occurrence." + hash);
    }

    private static string Identifier(string value, string parameter) =>
        string.IsNullOrWhiteSpace(value) || value.Length > InteractionContractLimits.Identifier
            ? throw new InteractionContractException("INVALID_IDENTIFIER", $"{parameter} is required and may contain at most {InteractionContractLimits.Identifier} characters.", parameter)
            : value.Trim();

    private static string UpperSha256(string value, string parameter) =>
        value is not { Length: 64 } || value.Any(character => !(char.IsAsciiDigit(character) || character is >= 'A' and <= 'F'))
            ? throw new InteractionContractException("INVALID_SHA256", $"{parameter} must be an uppercase SHA-256 value.", parameter)
            : value;
}
