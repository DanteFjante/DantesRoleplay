using DantesRoleplay.CatalogNamespaces;
using System.ComponentModel;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.RegistryAdministration;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.Extensions.DependencyInjection;
using DantesRoleplay.StateSpaceAdministration;
using DantesRoleplay.ComponentTypeAdministration;
using DantesRoleplay.LegacyStateAdoption;
using DantesRoleplay.TriggerScheduling;
using DantesRoleplay.Knowledge;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Blobs;
using DantesRoleplay.SystemFeedback;
using DantesRoleplay.ApplicationExecution;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>Ruleset-neutral write dispatcher retained under the existing public <c>commit</c> verb.</summary>
[McpServerToolType]
public sealed class CommitMcpTool
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [McpServerTool(Name = "commit")]
    [Description("Change state with application.action.execute, feedback, system.application.register, system.source.register, system.extension.register, system.component-type.register, system.namespace.register, system.application.activate, system.state-space.create, system.state-space.upgrade, system.state-space.adopt-legacy, system.world-state.sync, system.interaction-execute, system.interaction-recipe-review, system.trigger-scheduling, system.knowledge-state.sync, system.blob-upload.begin, or system.blob-upload.finalize. Generic component, effects, mechanic, and action writes are retired. Use query(kind: \"capabilities\") for each current closed payload contract.")]
    public async Task<ToolEnvelope> CommitAsync(
        IOperationLog log,
        string kind,
        string payload,
        string intent = "",
        string[]? proceduresUsed = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default,
        IRegistryAdministrationService? registryAdministration = null,
        ICatalogNamespaceRegistry? catalogNamespaces = null,
        IPrivateOperatorRequestAuthorizer? privateOperator = null,
        IApplicationActivationService? applicationActivations = null,
        IStateSpaceAdministrationService? stateSpaceAdministration = null,
        IComponentTypeAdministrationService? componentTypeAdministration = null,
        ILegacyStateAdoptionService? legacyStateAdoption = null,
        IInteractionGateway? interactionGateway = null,
        IInteractionRecipeReviewService? interactionRecipeReviews = null,
        ITriggerSchedulingAdministrationService? triggerSchedulingAdministration = null,
        IReviewedKnowledgeStateSynchronizer? knowledgeStateSynchronization = null,
        IApplicationWorldAuthoringSynchronizer? worldStateSynchronization = null,
        IBlobTransferService? blobTransfers = null,
        ISystemFeedbackService? feedback = null,
        IServiceScopeFactory? serviceScopes = null,
        IApplicationActionRunner? applicationActions = null)
    {
        var normalizedKind = kind?.Trim().ToLowerInvariant() ?? string.Empty;
        var spec = McpVerbCatalog.DispatchCommit(normalizedKind);
        if (spec is null)
        {
            return await ToolRunner.RunAsync(log, "commit", intent, $"commit:{normalizedKind}", proceduresUsed,
                () => Task.FromResult(ToolOutcome.Fail("UNKNOWN_KIND", $"Unknown commit kind '{kind}'. Valid kinds: {string.Join(", ", McpVerbCatalog.CommitKindNames)}.", "query(kind: \"capabilities\")", "Rejected unknown commit kind.")),
                consumesReadEvidence: false);
        }
        if (dryRun && !spec.Descriptor.Operations.SupportsPreview)
        {
            return await ToolRunner.RunAsync(log, "commit", intent, $"commit:{normalizedKind}", proceduresUsed,
                () => Task.FromResult(ToolOutcome.Fail("NOT_SUPPORTED", $"Dry run is not supported for '{normalizedKind}'.", McpVerbCatalog.CommitCall(normalizedKind), "Rejected unsupported dry run.")),
                consumesReadEvidence: false);
        }
        return normalizedKind switch
        {
            "application.action.execute" => await new ApplicationActionExecutionHandler().ExecuteAsync(
                applicationActions, privateOperator, log, payload, intent, proceduresUsed, cancellationToken),
            "feedback" => await CommitFeedbackAsync(feedback, log, payload, intent, proceduresUsed, cancellationToken),
            "system.application.register" => await new SystemRegistryCommitHandler().RegisterApplicationAsync(
                registryAdministration, privateOperator, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.source.register" => await new SystemRegistryCommitHandler().RegisterSourceAsync(
                registryAdministration, privateOperator, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.namespace.register" => await new CatalogNamespaceHandler().RegisterAsync(
                catalogNamespaces, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.extension.register" => await new SystemRegistryCommitHandler().RegisterExtensionAsync(
                registryAdministration, privateOperator, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.component-type.register" => await new SystemComponentTypeHandler().RegisterAsync(componentTypeAdministration, privateOperator, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.application.activate" => await new SystemApplicationActivationHandler().ActivateAsync(
                applicationActivations, privateOperator, log, payload, intent, proceduresUsed, dryRun,
                serviceScopes, cancellationToken),
            "system.state-space.create" => await new SystemStateSpaceHandler().CreateAsync(
                stateSpaceAdministration, privateOperator, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.state-space.upgrade" => await new SystemStateSpaceHandler().UpgradeAsync(
                stateSpaceAdministration, privateOperator, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.state-space.adopt-legacy" => await new SystemLegacyStateAdoptionHandler().AdoptAsync(
                legacyStateAdoption, privateOperator, log, payload, intent, proceduresUsed, dryRun, cancellationToken),
            "system.world-state.sync" => await new SystemWorldStateHandler().SynchronizeAsync(
                worldStateSynchronization, privateOperator, log, payload, intent, proceduresUsed, dryRun,
                cancellationToken),
            "system.interaction-execute" => await new SystemInteractionHandler().ExecuteAsync(
                interactionGateway, privateOperator, log, payload, intent, proceduresUsed, cancellationToken),
            "system.interaction-recipe-review" => await new SystemInteractionHandler().ReviewRecipeAsync(
                interactionRecipeReviews, privateOperator, log, payload, intent, proceduresUsed, cancellationToken),
            "system.trigger-scheduling" => await new SystemTriggerSchedulingHandler().CommitAsync(
                triggerSchedulingAdministration, privateOperator, log, payload, intent, proceduresUsed, dryRun,
                cancellationToken),
            "system.knowledge-state.sync" => await new SystemKnowledgeStateHandler().SynchronizeAsync(
                knowledgeStateSynchronization, privateOperator, log, payload, intent, proceduresUsed, dryRun,
                cancellationToken),
            "system.blob-upload.begin" => await new SystemBlobHandler().BeginAsync(
                blobTransfers, privateOperator, log, payload, intent, proceduresUsed, cancellationToken),
            "system.blob-upload.finalize" => await new SystemBlobHandler().FinalizeAsync(
                blobTransfers, privateOperator, log, payload, intent, proceduresUsed, cancellationToken),
            _ => throw new InvalidOperationException($"Unhandled generic commit kind '{normalizedKind}'.")
        };
    }

    private static async Task<ToolEnvelope> CommitFeedbackAsync(
        ISystemFeedbackService? feedback,
        IOperationLog log,
        string payload,
        string intent,
        string[]? procedures,
        CancellationToken cancellationToken)
    {
        if (feedback is null)
            return await InvalidAsync(log, "feedback", "Feedback reporting is unavailable because its store is not registered.", intent, procedures);

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "operation", "requestToken", "category", "impact", "summary", "observed",
                "expected", "reproductionSteps", "relatedOperationIds", "relatedProcedureIds"
            };
            var required = new HashSet<string>(StringComparer.Ordinal)
            {
                "operation", "requestToken", "category", "impact", "summary", "observed"
            };
            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().Any(property => !allowed.Contains(property.Name))
                || required.Any(name => !root.TryGetProperty(name, out _))
                || !root.TryGetProperty("operation", out var operation)
                || operation.ValueKind != JsonValueKind.String
                || operation.GetString() != "submit")
                return await InvalidAsync(log, "feedback", "Feedback payload requires operation submit and its exact closed shape.", intent, procedures);

            var request = root.Deserialize<SystemFeedbackSubmitRequest>(JsonOptions);
            return request is null
                ? await InvalidAsync(log, "feedback", "Feedback payload could not be read.", intent, procedures)
                : await new SystemFeedbackHandler().SubmitAsync(feedback, request, intent, procedures, cancellationToken);
        }
        catch (JsonException exception)
        {
            return await InvalidAsync(log, "feedback", exception.Message, intent, procedures);
        }
    }

    private static Task<ToolEnvelope> InvalidAsync(IOperationLog log, string kind, string message, string intent, string[]? procedures) =>
        ToolRunner.RunAsync(log, "commit", intent, $"commit:{kind}", procedures,
            () => Task.FromResult(ToolOutcome.Fail("INVALID_PAYLOAD", message, McpVerbCatalog.CommitCall(kind), $"Rejected {kind} payload.")),
            consumesReadEvidence: false);

}
