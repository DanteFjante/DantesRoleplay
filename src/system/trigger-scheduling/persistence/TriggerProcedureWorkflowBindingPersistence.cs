using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;

namespace DantesRoleplay.TriggerScheduling;

internal static class TriggerProcedureWorkflowBindingPersistence
{
    internal static OneTimeTriggerWorkflowBindingRecord OneTime(
        OneTimeTriggerDefinition definition)
    {
        var target = definition.ProcedureWorkflow ?? throw new InvalidDataException("A workflow trigger is missing its admitted binding.");
        var data = Data(target);
        return new OneTimeTriggerWorkflowBindingRecord
        {
            ApplicationId = definition.ApplicationId.Value,
            TriggerId = definition.Id,
            TriggerVersion = definition.Version,
            PrincipalReference = data.PrincipalReference,
            AuthenticationMethod = data.AuthenticationMethod,
            ApplicationRevision = data.ApplicationRevision,
            ApplicationFingerprint = data.ApplicationFingerprint,
            BaseApplicationsJson = data.BaseApplicationsJson,
            StateSpaceId = data.StateSpaceId,
            GrantReference = data.GrantReference,
            StateRevision = data.StateRevision,
            DefinitionId = data.DefinitionId,
            DefinitionVersion = data.DefinitionVersion,
            DefinitionFingerprint = data.DefinitionFingerprint,
            ExecutionRequestJson = data.ExecutionRequestJson,
            ResultSchemaJson = data.ResultSchemaJson,
            ResultSchemaFingerprint = data.ResultSchemaFingerprint,
            MaximumOperations = data.MaximumOperations,
            RuntimeWindowSeconds = data.RuntimeWindowSeconds,
            BindingFingerprint = data.BindingFingerprint
        };
    }

    internal static ObservationTriggerWorkflowBindingRecord Observation(
        ObservationTriggerDefinition definition)
    {
        var target = definition.ProcedureWorkflow ?? throw new InvalidDataException("A workflow trigger is missing its admitted binding.");
        var data = Data(target);
        return new ObservationTriggerWorkflowBindingRecord
        {
            ApplicationId = definition.ApplicationId.Value,
            TriggerId = definition.Id,
            TriggerVersion = definition.Version,
            PrincipalReference = data.PrincipalReference,
            AuthenticationMethod = data.AuthenticationMethod,
            ApplicationRevision = data.ApplicationRevision,
            ApplicationFingerprint = data.ApplicationFingerprint,
            BaseApplicationsJson = data.BaseApplicationsJson,
            StateSpaceId = data.StateSpaceId,
            GrantReference = data.GrantReference,
            StateRevision = data.StateRevision,
            DefinitionId = data.DefinitionId,
            DefinitionVersion = data.DefinitionVersion,
            DefinitionFingerprint = data.DefinitionFingerprint,
            ExecutionRequestJson = data.ExecutionRequestJson,
            ResultSchemaJson = data.ResultSchemaJson,
            ResultSchemaFingerprint = data.ResultSchemaFingerprint,
            MaximumOperations = data.MaximumOperations,
            RuntimeWindowSeconds = data.RuntimeWindowSeconds,
            BindingFingerprint = data.BindingFingerprint
        };
    }

    internal static bool Same(OneTimeTriggerWorkflowBindingRecord? row, TriggerProcedureWorkflowTarget? target) =>
        row is null ? target is null : target is not null && row.BindingFingerprint == target.Fingerprint;

    internal static bool Same(ObservationTriggerWorkflowBindingRecord? row, TriggerProcedureWorkflowTarget? target) =>
        row is null ? target is null : target is not null && row.BindingFingerprint == target.Fingerprint;

    internal static SystemTaskDurableTriggerTarget? Materialize(
        string bindingId,
        int bindingVersion,
        string occurrenceId,
        DateTimeOffset admittedAt,
        OneTimeTriggerWorkflowBindingRecord row) =>
        Materialize(bindingId, bindingVersion, occurrenceId, admittedAt, Data(row));

    internal static SystemTaskDurableTriggerTarget? Materialize(
        string bindingId,
        int bindingVersion,
        string occurrenceId,
        DateTimeOffset admittedAt,
        ObservationTriggerWorkflowBindingRecord row) =>
        Materialize(bindingId, bindingVersion, occurrenceId, admittedAt, Data(row));

    private static SystemTaskDurableTriggerTarget? Materialize(string bindingId, int bindingVersion,
        string occurrenceId, DateTimeOffset admittedAt, BindingData data)
    {
        if (data.ResultSchemaJson is null || data.ResultSchemaFingerprint is null)
            return null;
        var applicationId = ApplicationIdentifier.Parse(data.ApplicationId);
        var bases = JsonSerializer.Deserialize<string[]>(data.BaseApplicationsJson)
            ?.Select(ApplicationIdentifier.Parse).ToArray() ?? [];
        var identity = SystemTaskDurableTriggerTarget.Identity(bindingId, bindingVersion, occurrenceId,
            data.PrincipalReference, data.ApplicationId, data.StateSpaceId);
        var deadline = admittedAt.AddSeconds(data.RuntimeWindowSeconds).UtcDateTime;
        var host = new InteractionInvocationHost(
            TrustedPrincipalContext.VerifiedPrincipal(data.PrincipalReference, data.AuthenticationMethod),
            new ApplicationRevision(applicationId, data.ApplicationRevision, data.ApplicationFingerprint, bases),
            data.StateSpaceId, data.GrantReference, identity.CommandId, data.StateRevision,
            InteractionExecutionProfile.Workflow,
            new InteractionInvocationBudget(data.MaximumOperations, deadline));
        var definition = new SystemTaskSelectedDefinition(data.DefinitionId, data.DefinitionVersion,
            data.DefinitionFingerprint);
        var target = TriggerProcedureWorkflowTarget.Create(host, definition, data.ExecutionRequestJson,
            data.ResultSchemaJson, TimeSpan.FromSeconds(data.RuntimeWindowSeconds));
        if (target.Fingerprint != data.BindingFingerprint ||
            target.ResultSchemaFingerprint != data.ResultSchemaFingerprint)
            throw new InvalidDataException("The retained trigger workflow binding fingerprint is invalid.");
        return new SystemTaskDurableTriggerTarget(bindingId, bindingVersion, data.BindingFingerprint,
            occurrenceId, host, definition, data.ExecutionRequestJson, data.ResultSchemaJson);
    }

    private static BindingData Data(TriggerProcedureWorkflowTarget target)
    {
        var host = target.InvocationHost;
        return new(host.Principal.PrincipalId, host.Principal.AuthenticationMethod,
            host.ApplicationRevision.ApplicationId.Value, host.ApplicationRevision.Revision,
            host.ApplicationRevision.Fingerprint,
            InteractionCanonicalJson.Canonicalize(JsonSerializer.Serialize(
                host.ApplicationRevision.BaseRealmApps())),
            host.StateSpaceId!, host.GrantReference, host.StateRevision!,
            target.SelectedDefinition.ExactDefinitionId, target.SelectedDefinition.Version,
            target.SelectedDefinition.Fingerprint, target.ExecutionRequestJson,
            target.ResultSchemaJson, target.ResultSchemaFingerprint,
            host.Budget.MaximumOperations, checked((int)target.RuntimeWindow.TotalSeconds), target.Fingerprint);
    }

    private static BindingData Data(OneTimeTriggerWorkflowBindingRecord row) => new(
        row.PrincipalReference, row.AuthenticationMethod, row.ApplicationId, row.ApplicationRevision,
        row.ApplicationFingerprint, row.BaseApplicationsJson, row.StateSpaceId, row.GrantReference,
        row.StateRevision, row.DefinitionId, row.DefinitionVersion, row.DefinitionFingerprint,
        row.ExecutionRequestJson, row.ResultSchemaJson, row.ResultSchemaFingerprint,
        row.MaximumOperations, row.RuntimeWindowSeconds, row.BindingFingerprint);

    private static BindingData Data(ObservationTriggerWorkflowBindingRecord row) => new(
        row.PrincipalReference, row.AuthenticationMethod, row.ApplicationId, row.ApplicationRevision,
        row.ApplicationFingerprint, row.BaseApplicationsJson, row.StateSpaceId, row.GrantReference,
        row.StateRevision, row.DefinitionId, row.DefinitionVersion, row.DefinitionFingerprint,
        row.ExecutionRequestJson, row.ResultSchemaJson, row.ResultSchemaFingerprint,
        row.MaximumOperations, row.RuntimeWindowSeconds, row.BindingFingerprint);

    private sealed record BindingData(string PrincipalReference, string AuthenticationMethod,
        string ApplicationId, int ApplicationRevision, string ApplicationFingerprint,
        string BaseApplicationsJson, string StateSpaceId, string GrantReference, string StateRevision,
        string DefinitionId, int DefinitionVersion, string DefinitionFingerprint,
        string ExecutionRequestJson, string? ResultSchemaJson, string? ResultSchemaFingerprint,
        int MaximumOperations, int RuntimeWindowSeconds,
        string BindingFingerprint);

    private static IEnumerable<string> BaseRealmApps(this ApplicationRevision revision) =>
        revision.BaseApplications.Select(value => value.Value);
}
