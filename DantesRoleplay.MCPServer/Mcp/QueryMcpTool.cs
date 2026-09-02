using System.ComponentModel;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Events;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using DantesRoleplay.SystemFeedback;
using DantesRoleplay.Information;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.Authorization;
using DantesRoleplay.Sources;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Projections;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.StateSpaceAdministration;
using DantesRoleplay.Interactions;
using DantesRoleplay.TriggerScheduling;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Blobs;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>
/// The read side of the three-verb MCP surface. The preserved tool classes remain the behaviour
/// implementation; this class supplies the public protocol and dispatches by the closed kind set.
///
/// The kind list is <see cref="McpVerbCatalog.QueryKinds"/> and nothing else — the switch below is
/// asserted against it in both directions by a guard test, so this tool can never accept a kind
/// the catalog hides, nor advertise one it cannot serve.
/// </summary>
[McpServerToolType]
public sealed class QueryMcpTool
{
    [McpServerTool(Name = "query")]
    [Description(
        "Read anything in this system. kind is one of: capabilities, procedures, categories, world, entities, graph, information-answer, information-actions, system.audience-context, " +
        "mechanics, event-types, events, subscriptions, notifications, feedback, system.applications, system.sources, system.application-preview, system.dependencies, system.catalogs, system.catalog.browse, system.catalog.search, system.catalog.record, system.feature-search, system.interaction-plan, system.interaction-receipt, system.interaction-recipes, system.trigger-scheduling, system.blobs, namespaces, history. Omit id for a list or search; " +
        "pass id for one record in full. When you are unsure what a kind takes or what a commit payload looks like, call " +
        "query(kind: \"capabilities\") — it is the exact catalog. Irrelevant filters are ignored unless a fixed query kind explicitly rejects them. Never changes state.")]
    public async Task<ToolEnvelope> QueryAsync(
        IProcedureStore procedures,
        IWorldStore world,
        IGraphProjectionReader graphs,
        IMechanicStore mechanics,
        IEventTypeStore eventTypes,
        ISubscriptionStore subscriptions,
        IEventLedger events,
        IOperationLog log,
        INotificationStore notifications,
        [Description(
            "Closed kind: capabilities, procedures, categories, world, entities, graph, mechanics, event-types, events, "
            + "subscriptions, notifications, feedback, information-answer, information-actions, system.audience-context, system.applications, system.sources, system.application-preview, system.dependencies, system.catalogs, system.catalog.browse, system.catalog.search, system.catalog.record, system.feature-search, system.interaction-plan, system.interaction-receipt, system.interaction-recipes, system.trigger-scheduling, system.blobs, or history.")]
        string kind,
        [Description("Full-record id for procedures, mechanics, or one entity.")] string? id = null,
        [Description("Entity ids for a full batch read.")] string[]? ids = null,
        [Description("Historical version, only when id is supplied for procedures or mechanics.")]
        int? version = null,
        [Description("Search text for procedures or mechanics.")] string? query = null,
        [Description("Entity name substring.")] string? nameQuery = null,
        [Description("Entity component definition filter.")] string? withDefinitionId = null,
        [Description("Category branch for procedures, mechanics, or categories. A record query includes this node and its descendants.")] string? category = null,
        [Description("Catalog to browse with kind categories: procedures or mechanics.")] string? catalog = null,
        [Description("Ruleset preference for mechanics.")] string? scope = null,
        [Description("Include deprecated and archived records.")] bool includeInactive = false,
        [Description("Maximum results for procedures, entities, mechanics or history.")]
        int? limit = null,
        [Description("Number of example entities for the world query.")] int? sample = null,
        [Description("Graph query: required selected component-definition ids, 1–12 distinct ids.")]
        string[]? componentIds = null,
        [Description("Graph query: required descendant-containment traversal depth, 0–2.")]
        int? containmentDepth = null,
        [Description("Graph query: required relationship kinds, 0–12 distinct ids; empty means containment-only.")]
        string[]? relationshipKinds = null,
        [Description("Graph query: required selected-relationship traversal depth, 0–2.")]
        int? relationshipDepth = null,
        [Description("Graph query: optional node cap, 1–100; default 50.")]
        int? maxNodes = null,
        [Description("Graph query: optional edge cap, 0–200; default 100.")]
        int? maxEdges = null,
        [Description("Only failed history records.")] bool failuresOnly = false,
        [Description("History tool filter.")] string? tool = null,
        [Description("History subject filter.")] string? subject = null,
        [Description("Event chain filter: every event from one committed world change.")]
        string? correlationId = null,
        [Description("Event filter: events directly caused by one earlier event.")]
        string? causationId = null,
        [Description("Event filter: the audited operation an event belongs to.")]
        string? rootOperationId = null,
        [Description("Event filter: registered event type id, e.g. world.component.replaced.")]
        string? type = null,
        [Description("Event filter: every event concerning one entity.")] string? entityId = null,
        [Description("Event paging: exclusive lower bound on sequence within a chain.")]
        int? afterSequence = null,
        [Description("Event filter: inclusive ISO-8601 UTC lower bound, e.g. 2026-08-19T14:30:00Z.")]
        string? from = null,
        [Description("Event filter: exclusive ISO-8601 UTC upper bound.")] string? to = null,
        [Description("Notification filter: unread, read, or archived.")] string? state = null,
        [Description("Notification filter: dotted topic, e.g. combat.wound.")] string? topic = null,
        [Description("Feedback filter: blocked, degraded, minor, or none.")] string? impact = null,
        [Description("Knowledge answer: bounded natural-language question.")] string? question = null,
        [Description("Generic information answer: required authorized information scope.")] string? scopeId = null,
        [Description("Optional exact source IDs for information answers or reviewed application base-source previews; may be combined with extensionIds for operator installation.")] string[]? sourceIds = null,
        [Description("Application preview: optional exact registered extension IDs; omit to preview every compatible installed extension.")] string[]? extensionIds = null,
        [Description("System catalog queries: required non-system application ID.")] string? applicationId = null,
        [Description("System catalog queries: application-declared collection ID.")] string? collection = null,
        [Description("System catalog browse/search: slash-separated logical branch; empty means root.")] string? branch = null,
        [Description("System catalog browse/search: authenticated continuation cursor.")] string? cursor = null,
        [Description("System catalog search: optional record-kind filters.")] string[]? kinds = null,
        [Description("System catalog search: optional record-status filters.")] string[]? statuses = null,
        [Description("System catalog and feature search: optional namespace ID; includes descendant namespaces.")] string? namespaceId = null,
        [Description("Operator catalog diagnostics: include shadowed extension records and resolution evidence.")] bool includeShadowed = false,
        [Description("System catalog browse/search page size, 1–100; default 25.")] int? pageSize = null,
        [Description("System dependency impact: include indirect declared dependents; default true.")] bool? transitive = null,
        [Description("Interaction queries: application-bound state-space ID.")] string? stateSpaceId = null,
        [Description("Interaction plan: closed request JSON containing operation (resolve or submit), stateSpaceId, sessionContextId, intent, and proposal only for submit.")] string? request = null,
        [Description("Interaction recipes: optional closed status candidate, verified, stale, or retired.")] string? status = null,
        [Description("Trigger scheduling: overview, structures, sources, devices, one-time, recurring, conditional, observation-triggers, observations, fires, or phone-principal.")] string? resource = null,
        CancellationToken cancellationToken = default,
        ISystemFeedbackService? feedback = null,
        IInformationAnswerCoordinator? informationAnswers = null,
        IInformationActionCoordinator? informationActions = null,
        IPublicApplicationCatalogProvider? publicCatalogs = null,
        ICatalogNamespaceRegistry? catalogNamespaces = null,
        IApplicationRegistry? applications = null,
        ISourceRegistry? sources = null,
        ISourceScanReceiptStore? sourceScans = null,
        IPrivateOperatorRequestAuthorizer? privateOperator = null,
        IApplicationPreviewService? applicationPreviews = null,
        IProjectionImpactService? projectionImpacts = null,
        IApplicationActivationReader? applicationActivations = null,
        IStateSpaceAdministrationReader? stateSpaceAdministration = null,
        IInteractionGateway? interactionGateway = null,
        IInteractionRecipeStore? interactionRecipes = null,
        ITriggerSchedulingAdministrationService? triggerSchedulingAdministration = null,
        ISystemCapabilityCatalog? systemCapabilities = null,
        ILocalKnowledgeSeatProvider? localKnowledgeSeats = null,
        IAuthorizedKnowledgeAudiencePolicy? knowledgeAudiences = null,
        IKnowledgeApplicationBindingResolver? knowledgeBindings = null,
        IKnowledgeActorParticipationVerifier? knowledgeParticipation = null,
        IApplicationRegistry? applicationEntityApplications = null,
        IStateSpaceRegistry? applicationEntityStateSpaces = null,
        IEntityComponentStore? applicationEntities = null,
        IBlobTransferService? blobTransfers = null)
    {
        var normalizedKind = kind?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!McpVerbCatalog.IsQueryKind(normalizedKind))
        {
            return await ToolRunner.RunAsync(log, "query", () =>
                Task.FromResult(ToolOutcome.Fail(
                    "UNKNOWN_KIND",
                    $"Unknown query kind '{kind}'. Valid kinds: "
                    + $"{string.Join(", ", McpVerbCatalog.QueryKindNames)}.",
                    "query(kind: \"capabilities\")",
                    $"Rejected query kind '{kind}'.")));
        }

        if (normalizedKind == "capabilities")
        {
            return await ToolRunner.RunAsync(log, "query", () =>
                Task.FromResult(ToolOutcome.Ok(
                    McpVerbCatalog.Catalog(),
                    "Returned the full query and commit catalog.",
                    "query(kind: \"procedures\") — read the contract governing what you are about to do.",
                    "query(kind: \"world\") — see what the world already holds before adding to it.")));
        }

        using var dispatch = ToolRunner.EnterProtocol("query", normalizedKind);

        return normalizedKind switch
        {
            "procedures" when string.IsNullOrWhiteSpace(id) =>
                await new ProcedureHandler().FindProceduresAsync(
                    procedures, log, query, category, includeInactive, limit, cancellationToken),
            "procedures" =>
                await new ProcedureHandler().GetProcedureAsync(
                    procedures, log, id!, version, cancellationToken),
            "categories" =>
                await new CategoryQueryHandler().BrowseAsync(
                    procedures, mechanics, log, catalog, category, includeInactive, cancellationToken),
            "world" =>
                await new WorldHandler().DescribeWorldAsync(
                    world, log, sample ?? 10, cancellationToken),
            "entities" when !string.IsNullOrWhiteSpace(applicationId)
                && !string.IsNullOrWhiteSpace(stateSpaceId)
                && applicationEntityApplications is not null
                && applicationEntityStateSpaces is not null
                && applicationEntities is not null =>
                await new ApplicationEntityQueryHandler().GetEntitiesAsync(
                    applicationEntityApplications,
                    applicationEntityStateSpaces,
                    applicationEntities,
                    log,
                    applicationId,
                    stateSpaceId,
                    string.IsNullOrWhiteSpace(id) ? ids : [id!],
                    nameQuery,
                    withDefinitionId,
                    limit ?? 50,
                    cancellationToken),
            "entities" when !string.IsNullOrWhiteSpace(applicationId) || !string.IsNullOrWhiteSpace(stateSpaceId) =>
                await ToolRunner.RunAsync(log, "query", () => Task.FromResult(ToolOutcome.Fail(
                    "APPLICATION_ENTITY_SCOPE_INVALID",
                    "Application entity reads require applicationId, stateSpaceId, and an available application ECS reader.",
                    "query(kind: \"entities\", applicationId: \"...\", stateSpaceId: \"...\", id: \"...\")",
                    "Rejected an incomplete application entity query."))),
            "entities" when string.IsNullOrWhiteSpace(nameQuery)
                && string.IsNullOrWhiteSpace(withDefinitionId)
                && (string.IsNullOrWhiteSpace(id) ? ids : [id!]) is { Length: > 0 } exactIds
                && applicationEntityApplications is not null
                && applicationEntityStateSpaces is not null
                && applicationEntities is not null =>
                await new ApplicationEntityQueryHandler().FindExactEntitiesAsync(
                    applicationEntityApplications,
                    applicationEntityStateSpaces,
                    applicationEntities,
                    log,
                    exactIds,
                    limit ?? 50,
                    cancellationToken),
            "entities" =>
                await new WorldHandler().GetEntitiesAsync(
                    world, log,
                    string.IsNullOrWhiteSpace(id) ? ids : [id!],
                    nameQuery,
                    withDefinitionId,
                    limit ?? 50,
                    cancellationToken),
            "graph" => await new GraphQueryHandler().GetGraphAsync(
                graphs,
                log,
                new GraphQuery(id ?? string.Empty, componentIds, containmentDepth, relationshipKinds, relationshipDepth, maxNodes, maxEdges),
                cancellationToken),
            "information-answer" when informationAnswers is not null => await new InformationHandler().AnswerAsync(
                informationAnswers, log, new InformationAnswerRequest(scopeId ?? string.Empty, question ?? string.Empty, sourceIds, limit ?? 12), cancellationToken),
            "information-answer" => await ToolRunner.RunAsync(log, "query", () => Task.FromResult(ToolOutcome.Fail("INFORMATION_UNAVAILABLE", "Generic information answering is not configured.", "orient()", "Information answering was unavailable."))),
            "information-actions" when informationActions is not null => await new InformationHandler().ListActionsAsync(informationActions, log, scopeId ?? string.Empty, cancellationToken),
            "information-actions" => await ToolRunner.RunAsync(log, "query", () => Task.FromResult(ToolOutcome.Fail("INFORMATION_UNAVAILABLE", "Generic information actions are not configured.", "orient()", "Information action contracts were unavailable."))),
            "mechanics" =>
                await new MechanicHandler().FindMechanicsAsync(
                    mechanics, log, id, version, query, category, scope, includeInactive, limit, cancellationToken),
            "event-types" => await new EventTypeHandler().FindAsync(eventTypes, log, id, version, query, category, scope, includeInactive, limit, cancellationToken),
            "events" => await new EventQueryHandler().FindAsync(
                events, log, id, correlationId, causationId, rootOperationId, type, entityId,
                afterSequence, from, to, limit, cancellationToken),
            "subscriptions" => await new SubscriptionHandler().FindAsync(subscriptions, log, id, version, query, category, scope, includeInactive, limit, cancellationToken),
            "notifications" => await new NotificationHandler().FindAsync(
                notifications, log, id, state, topic, entityId, correlationId, from, to, limit, cancellationToken),
            "feedback" when feedback is not null => await new SystemFeedbackHandler().FindAsync(
                feedback, log, id, category, impact, state, from, to, limit, cancellationToken),
            "feedback" => await ToolRunner.RunAsync(log, "query", () => Task.FromResult(ToolOutcome.Fail("FEEDBACK_UNAVAILABLE", "Feedback reporting is not configured.", "orient()", "Feedback query was unavailable."))),
            "system.audience-context" => await new SystemAudienceContextHandler().CurrentAsync(
                localKnowledgeSeats, knowledgeAudiences, knowledgeBindings, knowledgeParticipation,
                log, cancellationToken),
            "system.applications" => await new SystemRegistryQueryHandler().ApplicationsAsync(
                systemCapabilities, privateOperator, log, applicationId, limit, cancellationToken),
            "system.sources" => await new SystemRegistryQueryHandler().SourcesAsync(
                applications, sources, sourceScans, privateOperator, log, applicationId, id, limit),
            "system.application-preview" => await new SystemApplicationPreviewHandler().PreviewAsync(
                applicationPreviews, privateOperator, log, applicationId, sourceIds, extensionIds, limit, cancellationToken),
            "system.dependencies" => await new SystemDependencyHandler().InspectAsync(
                projectionImpacts, privateOperator, log, applicationId, id, transitive, limit),
            "system.catalogs" => await new SystemCatalogHandler().ListAsync(
                publicCatalogs ?? new EmptyPublicApplicationCatalogProvider(), log, applicationId),
            "system.catalog.browse" => await new SystemCatalogHandler().BrowseAsync(
                publicCatalogs ?? new EmptyPublicApplicationCatalogProvider(), log, applicationId, collection, branch, pageSize, cursor),
            "system.catalog.search" => await new SystemCatalogHandler().SearchAsync(
                publicCatalogs ?? new EmptyPublicApplicationCatalogProvider(), log, applicationId, query, collection, branch, kinds, statuses, pageSize, cursor, namespaceId, includeShadowed),
            "system.catalog.record" => await new SystemCatalogHandler().RecordAsync(
                publicCatalogs ?? new EmptyPublicApplicationCatalogProvider(), log, applicationId, collection, id),
            "system.feature-search" => await new SystemInteractionHandler().SearchAsync(
                interactionGateway, privateOperator, log, applicationId, query, id, limit, namespaceId, cancellationToken),
            "system.interaction-plan" => await new SystemInteractionHandler().PlanAsync(
                interactionGateway, privateOperator, log, applicationId, request, cancellationToken),
            "system.interaction-receipt" => await new SystemInteractionHandler().ReceiptAsync(
                interactionGateway, privateOperator, log, applicationId, stateSpaceId, id, cancellationToken),
            "system.interaction-recipes" => await new SystemInteractionHandler().RecipesAsync(
                interactionRecipes, privateOperator, log, applicationId, id, query, status, cursor, limit, cancellationToken),
            "system.trigger-scheduling" => await new SystemTriggerSchedulingHandler().QueryAsync(
                triggerSchedulingAdministration, privateOperator, log, applicationId, resource, id, limit, cancellationToken),
            "system.blobs" => await new SystemBlobHandler().QueryAsync(
                blobTransfers, privateOperator, log, id, cancellationToken),
            "namespaces" => await new CatalogNamespaceHandler().ListAsync(
                catalogNamespaces, log, query, id, includeInactive, limit),
            "history" =>
                await new HistoryQueryHandler().HistoryAsync(
                    log, limit ?? 20, failuresOnly, tool, subject, cancellationToken),
            _ => throw new InvalidOperationException($"Unhandled query kind '{kind}'.")
        };
    }

}
