using System.ComponentModel;
using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.RegistryAdministration;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.StateSpaceAdministration;
using DantesRoleplay.ComponentTypeAdministration;
using DantesRoleplay.World;
using DantesRoleplay.LegacyStateAdoption;
using DantesRoleplay.Interactions;
using DantesRoleplay.TriggerScheduling;
using DantesRoleplay.Knowledge;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>Ruleset-neutral write dispatcher retained under the existing public <c>commit</c> verb.</summary>
[McpServerToolType]
public sealed class CommitTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [McpServerTool(Name = "commit")]
    [Description("Change state with component, effects, mechanic, action, system.application.register, system.source.register, system.component-type.register, system.application.activate, system.state-space.create, system.state-space.upgrade, system.state-space.adopt-legacy, system.interaction-execute, system.interaction-recipe-review, system.trigger-scheduling, or system.knowledge-state.sync. Use query(kind: \"capabilities\") for each closed payload catalog.")]
    public async Task<ToolEnvelope> CommitAsync(
        IWorldStore world,
        IEffectApplier effects,
        IMechanicStore mechanics,
        IActionRunner actions,
        IOperationLog log,
        string kind,
        string payload,
        string intent = "",
        string[]? proceduresUsed = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default,
        IRegistryAdministrationService? registryAdministration = null,
        IPrivateOperatorRequestAuthorizer? privateOperator = null,
        IApplicationActivationService? applicationActivations = null,
        IStateSpaceAdministrationService? stateSpaceAdministration = null,
        IComponentTypeAdministrationService? componentTypeAdministration = null,
        ILegacyStateAdoptionService? legacyStateAdoption = null,
        IInteractionGateway? interactionGateway = null,
        IInteractionRecipeReviewService? interactionRecipeReviews = null,
        ITriggerSchedulingAdministrationService? triggerSchedulingAdministration = null,
        IReviewedKnowledgeStateSynchronizer? knowledgeStateSynchronization = null)
    {
        var normalizedKind = kind?.Trim().ToLowerInvariant() ?? string.Empty;
        var spec = VerbSurface.Commit(normalizedKind);
        if (spec is null)
        {
            return await ToolRunner.RunAsync(log, "commit", intent, $"commit:{normalizedKind}", proceduresUsed,
                () => Task.FromResult(ToolOutcome.Fail("UNKNOWN_KIND", $"Unknown commit kind '{kind}'. Valid kinds: {string.Join(", ", VerbSurface.CommitKindNames)}.", "query(kind: \"capabilities\")", "Rejected unknown commit kind.")),
                consumesReadEvidence: false);
        }
        if (dryRun && !spec.SupportsDryRun)
        {
            return await ToolRunner.RunAsync(log, "commit", intent, $"commit:{normalizedKind}", proceduresUsed,
                () => Task.FromResult(ToolOutcome.Fail("NOT_SUPPORTED", $"Dry run is not supported for '{normalizedKind}'.", VerbSurface.CommitCall(normalizedKind), "Rejected unsupported dry run.")),
                consumesReadEvidence: false);
        }
        return normalizedKind switch
        {
            "component" => await CommitGenericPayloadAsync(normalizedKind, payload, world, effects, mechanics, actions, log, intent, proceduresUsed, dryRun, cancellationToken),
            "effects" => await CommitGenericPayloadAsync(normalizedKind, payload, world, effects, mechanics, actions, log, intent, proceduresUsed, dryRun, cancellationToken),
            "mechanic" => await CommitGenericPayloadAsync(normalizedKind, payload, world, effects, mechanics, actions, log, intent, proceduresUsed, dryRun, cancellationToken),
            "action" => await CommitGenericPayloadAsync(normalizedKind, payload, world, effects, mechanics, actions, log, intent, proceduresUsed, dryRun, cancellationToken),
            "system.application.register" => await new SystemRegistryCommitTools().RegisterApplicationAsync(
                registryAdministration, privateOperator, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.source.register" => await new SystemRegistryCommitTools().RegisterSourceAsync(
                registryAdministration, privateOperator, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.component-type.register" => await new SystemComponentTypeTools().RegisterAsync(componentTypeAdministration, privateOperator, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.application.activate" => await new SystemApplicationActivationTools().ActivateAsync(
                applicationActivations, privateOperator, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.state-space.create" => await new SystemStateSpaceTools().CreateAsync(
                stateSpaceAdministration, privateOperator, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.state-space.upgrade" => await new SystemStateSpaceTools().UpgradeAsync(
                stateSpaceAdministration, privateOperator, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.state-space.adopt-legacy" => await new SystemLegacyStateAdoptionTools().AdoptAsync(
                legacyStateAdoption, privateOperator, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.interaction-execute" => await new SystemInteractionTools().ExecuteAsync(
                interactionGateway, privateOperator, log, payload, intent, proceduresUsed, cancellationToken),
            "system.interaction-recipe-review" => await new SystemInteractionTools().ReviewRecipeAsync(
                interactionRecipeReviews, privateOperator, log, payload, intent, proceduresUsed, cancellationToken),
            "system.trigger-scheduling" => await new SystemTriggerSchedulingTools().CommitAsync(
                triggerSchedulingAdministration, privateOperator, log, payload, intent, proceduresUsed, dryRun,
                cancellationToken),
            "system.knowledge-state.sync" => await new SystemKnowledgeStateTools().SynchronizeAsync(
                knowledgeStateSynchronization, privateOperator, log, payload, intent, proceduresUsed, dryRun,
                cancellationToken),
            _ => throw new InvalidOperationException($"Unhandled generic commit kind '{normalizedKind}'.")
        };
    }

    private static async Task<ToolEnvelope> CommitGenericPayloadAsync(
        string normalizedKind,
        string payload,
        IWorldStore world,
        IEffectApplier effects,
        IMechanicStore mechanics,
        IActionRunner actions,
        IOperationLog log,
        string intent,
        string[]? proceduresUsed,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return await InvalidAsync(log, normalizedKind, "payload must be a JSON object.", intent, proceduresUsed);
            return normalizedKind switch
            {
                "component" => await CommitComponentAsync(world, log, document.RootElement, intent, proceduresUsed, cancellationToken),
                "effects" => await CommitEffectsAsync(effects, log, document.RootElement, intent, proceduresUsed, dryRun, cancellationToken),
                "mechanic" => await CommitMechanicAsync(mechanics, log, document.RootElement, intent, proceduresUsed, dryRun, cancellationToken),
                "action" => await CommitActionAsync(actions, log, document.RootElement, intent, proceduresUsed, cancellationToken),
                _ => throw new InvalidOperationException($"Unhandled generic commit kind '{normalizedKind}'.")
            };
        }
        catch (JsonException exception)
        {
            return await InvalidAsync(log, normalizedKind, exception.Message, intent, proceduresUsed);
        }
    }

    private static async Task<ToolEnvelope> CommitComponentAsync(IWorldStore world, IOperationLog log, JsonElement payload, string intent, string[]? procedures, CancellationToken cancellationToken)
    {
        var value = payload.Deserialize<ComponentPayload>(JsonOptions);
        return value is null || string.IsNullOrWhiteSpace(value.Id) || string.IsNullOrWhiteSpace(value.Name) || string.IsNullOrWhiteSpace(value.Description)
            ? await InvalidAsync(log, "component", "component requires id, name, and description.", intent, procedures)
            : await new WorldTools().DefineComponentAsync(world, log, value.Id, value.Name, value.Description, value.Schema ?? string.Empty, intent, procedures, cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitEffectsAsync(IEffectApplier effects, IOperationLog log, JsonElement payload, string intent, string[]? procedures, bool dryRun, CancellationToken cancellationToken)
    {
        if (!payload.TryGetProperty("effects", out var element) || element.ValueKind != JsonValueKind.Array)
            return await InvalidAsync(log, "effects", "effects requires an effects array.", intent, procedures);
        var values = element.Deserialize<Effect[]>(JsonOptions);
        return values is null
            ? await InvalidAsync(log, "effects", "effects could not be read.", intent, procedures)
            : await new WorldTools().ApplyEffectsAsync(effects, log, values, intent, procedures, dryRun, cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitActionAsync(IActionRunner actions, IOperationLog log, JsonElement payload, string intent, string[]? procedures, CancellationToken cancellationToken)
    {
        var value = payload.Deserialize<ActionPayload>(JsonOptions);
        return value is null || string.IsNullOrWhiteSpace(value.Intent)
            ? await InvalidAsync(log, "action", "action requires intent.", intent, procedures)
            : await new ActionTools().RunActionAsync(actions, value.Intent, value.RoleEntityIds, value.Input ?? "{}", value.Scope, value.Seed, procedures, cancellationToken);
    }

    private static async Task<ToolEnvelope> CommitMechanicAsync(IMechanicStore mechanics, IOperationLog log, JsonElement payload, string intent, string[]? procedures, bool dryRun, CancellationToken cancellationToken)
    {
        var value = payload.Deserialize<MechanicPayload>(JsonOptions);
        return value is null || string.IsNullOrWhiteSpace(value.Id) || string.IsNullOrWhiteSpace(value.Category) || string.IsNullOrWhiteSpace(value.Name) || string.IsNullOrWhiteSpace(value.Source)
            ? await InvalidAsync(log, "mechanic", "mechanic requires id, category, name, and source.", intent, procedures)
            : await new MechanicTools().WriteMechanicAsync(mechanics, log, value.Id, value.Category, value.Name, value.Description ?? string.Empty, value.Matches ?? string.Empty, value.Requirements ?? "{}", value.Source, value.Scope ?? string.Empty, value.Status, value.ChangeNote ?? string.Empty, intent, procedures, dryRun, cancellationToken);
    }

    private static Task<ToolEnvelope> InvalidAsync(IOperationLog log, string kind, string message, string intent, string[]? procedures) =>
        ToolRunner.RunAsync(log, "commit", intent, $"commit:{kind}", procedures,
            () => Task.FromResult(ToolOutcome.Fail("INVALID_PAYLOAD", message, VerbSurface.CommitCall(kind), $"Rejected {kind} payload.")),
            consumesReadEvidence: false);

    private sealed record ComponentPayload(string? Id, string? Name, string? Description, string? Schema);
    private sealed record ActionPayload(string? Intent, Dictionary<string, string>? RoleEntityIds, string? Input, string? Scope, long? Seed);
    private sealed record MechanicPayload(string? Id, string? Category, string? Name, string? Source, string? Description, string? Matches, string? Requirements, string? Scope, string? Status, string? ChangeNote);
}
