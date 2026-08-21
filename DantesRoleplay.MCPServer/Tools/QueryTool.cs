using System.ComponentModel;
using DantesRoleplay.Campaign;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Events;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.Quest;
using DantesRoleplay.World;
using DantesRoleplay.SystemFeedback;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The read side of the three-verb MCP surface. The preserved tool classes remain the behaviour
/// implementation; this class supplies the public protocol and dispatches by the closed kind set.
///
/// The kind list is <see cref="VerbSurface.QueryKinds"/> and nothing else — the switch below is
/// asserted against it in both directions by a guard test, so this tool can never accept a kind
/// the catalog hides, nor advertise one it cannot serve.
/// </summary>
[McpServerToolType]
public sealed class QueryTool
{
    [McpServerTool(Name = "query")]
    [Description(
        "Read anything in this system. kind is one of: capabilities, procedures, categories, world, entities, graph, journey-plan, itinerary-plan, campaign-resume, session-recap, quest-summary, knowledge-answer, " +
        "mechanics, event-types, events, subscriptions, notifications, feedback, history. Omit id for a list or search; " +
        "pass id for one record in full. When you are unsure what a kind takes or what a commit payload looks like, call " +
        "query(kind: \"capabilities\") — it is the exact catalog. Irrelevant filters are ignored unless a fixed query kind explicitly rejects them. Never changes state.")]
    public async Task<ToolEnvelope> QueryAsync(
        IProcedureStore procedures,
        IWorldStore world,
        IGraphProjectionReader graphs,
        IJourneyPlanReader journeys,
        IModeAwareItineraryReader itineraries,
        ICampaignResumeReader campaignResumes,
        IQuestSummaryReader questSummaries,
        IMechanicStore mechanics,
        IEventTypeStore eventTypes,
        ISubscriptionStore subscriptions,
        IEventLedger events,
        IOperationLog log,
        INotificationStore notifications,
        [Description(
            "Closed kind: capabilities, procedures, categories, world, entities, graph, journey-plan, itinerary-plan, campaign-resume, session-recap, quest-summary, knowledge-answer, mechanics, event-types, events, "
            + "subscriptions, notifications, feedback, or history.")]
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
        [Description("Journey plan: required active world-root id.")] string? worldId = null,
        [Description("Journey plan: required active traveller id; its contained origin is derived.")] string? travellerId = null,
        [Description("Journey plan: required active destination location id.")] string? destinationId = null,
        [Description("Mode-aware itinerary: required active destination location id.")] string? destinationLocationId = null,
        [Description("Mode-aware itinerary: optional selected active ground conveyance id, initially co-located with the traveller.")] string? groundConveyanceId = null,
        [Description("Mode-aware itinerary: optional selected active aerial conveyance id, initially co-located with the traveller.")] string? aerialConveyanceId = null,
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
        [Description("Knowledge answer: configured campaign id. The configured local audience must match it.")] string? campaignId = null,
        [Description("Knowledge answer: bounded natural-language question.")] string? question = null,
        [Description("Knowledge answer: optional fact, rumour, secret, or clue filters.")] string[]? knowledgeKinds = null,
        [Description("Knowledge answer: optional canonical subject-id filters.")] string[]? knowledgeSubjectIds = null,
        [Description("Knowledge answer: optional world minute for canonical validity.")] long? asOfMinute = null,
        CancellationToken cancellationToken = default,
        [Description("Campaign resume only: require and compose the one current active session with the bounded C3 campaign view.")] bool includeSession = false,
        ICampaignSessionResumeReader? campaignSessionResumes = null,
        ICampaignSessionRecapReader? campaignSessionRecaps = null,
        ISystemFeedbackService? feedback = null,
        IAuthorizedKnowledgeAnswerCoordinator? knowledgeAnswers = null)
    {
        var normalizedKind = kind?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!VerbSurface.IsQueryKind(normalizedKind))
        {
            return await ToolRunner.RunAsync(log, "query", () =>
                Task.FromResult(ToolOutcome.Fail(
                    "UNKNOWN_KIND",
                    $"Unknown query kind '{kind}'. Valid kinds: "
                    + $"{string.Join(", ", VerbSurface.QueryKindNames)}.",
                    "query(kind: \"capabilities\")",
                    $"Rejected query kind '{kind}'.")));
        }

        if (normalizedKind == "capabilities")
        {
            return await ToolRunner.RunAsync(log, "query", () =>
                Task.FromResult(ToolOutcome.Ok(
                    VerbSurface.Catalog(),
                    "Returned the full query and commit catalog.",
                    "query(kind: \"procedures\") — read the contract governing what you are about to do.",
                    "query(kind: \"world\") — see what the world already holds before adding to it.")));
        }

        if (normalizedKind == "quest-summary" && !IsQuestSummaryRequest(
                id, ids, version, query, nameQuery, withDefinitionId, category, scope, includeInactive, limit, sample,
                componentIds, containmentDepth, relationshipKinds, relationshipDepth, maxNodes, maxEdges,
                worldId, travellerId, destinationId, destinationLocationId, groundConveyanceId, aerialConveyanceId,
                failuresOnly, tool, subject, correlationId, causationId, rootOperationId, type, entityId, afterSequence,
                from, to, state, topic))
        {
            return await ToolRunner.RunAsync(log, "query", () =>
                Task.FromResult(ToolOutcome.Fail(
                    "INVALID_QUEST_SUMMARY_QUERY",
                    "quest-summary accepts exactly one lowercase quest.* id and no other query filters.",
                    "query(kind: \"quest-summary\", id: \"quest....\")",
                    "Rejected quest summary query.")));
        }

        if (normalizedKind == "session-recap" && !IsSessionRecapRequest(
                id, ids, version, query, nameQuery, withDefinitionId, category, scope, includeInactive, limit, sample,
                componentIds, containmentDepth, relationshipKinds, relationshipDepth, maxNodes, maxEdges,
                worldId, travellerId, destinationId, destinationLocationId, groundConveyanceId, aerialConveyanceId,
                failuresOnly, tool, subject, correlationId, causationId, rootOperationId, type, entityId, afterSequence,
                from, to, state, topic, includeSession))
        {
            return await ToolRunner.RunAsync(log, "query", () =>
                Task.FromResult(ToolOutcome.Fail(
                    "INVALID_SESSION_RECAP_QUERY",
                    "session-recap accepts exactly one lowercase session.* id and no other query filters.",
                    "query(kind: \"session-recap\", id: \"session....\")",
                    "Rejected session recap query.")));
        }

        using var dispatch = ToolRunner.EnterProtocol("query", normalizedKind);

        return normalizedKind switch
        {
            "procedures" when string.IsNullOrWhiteSpace(id) =>
                await new ProcedureTools().FindProceduresAsync(
                    procedures, log, query, category, includeInactive, limit, cancellationToken),
            "procedures" =>
                await new ProcedureTools().GetProcedureAsync(
                    procedures, log, id!, version, cancellationToken),
            "categories" =>
                await new CategoryTools().BrowseAsync(
                    procedures, mechanics, log, catalog, category, includeInactive, cancellationToken),
            "world" =>
                await new WorldTools().DescribeWorldAsync(
                    world, log, sample ?? 10, cancellationToken),
            "entities" =>
                await new WorldTools().GetEntitiesAsync(
                    world, log,
                    string.IsNullOrWhiteSpace(id) ? ids : [id!],
                    nameQuery,
                    withDefinitionId,
                    limit ?? 50,
                    cancellationToken),
            "graph" => await new GraphTools().GetGraphAsync(
                graphs,
                log,
                new GraphQuery(id ?? string.Empty, componentIds, containmentDepth, relationshipKinds, relationshipDepth, maxNodes, maxEdges),
                cancellationToken),
            "journey-plan" => await new JourneyPlanTools().GetAsync(journeys, log, new JourneyPlanQuery(worldId ?? string.Empty, travellerId ?? string.Empty, destinationId ?? string.Empty), cancellationToken),
            "itinerary-plan" => await new ModeAwareItineraryTools().GetAsync(itineraries, log, new ModeAwareItineraryQuery(worldId ?? string.Empty, travellerId ?? string.Empty, destinationLocationId ?? string.Empty, groundConveyanceId, aerialConveyanceId), cancellationToken),
            "campaign-resume" when includeSession && campaignSessionResumes is not null => await new CampaignTools().ResumeSessionAsync(campaignSessionResumes, log, id ?? string.Empty, cancellationToken),
            "campaign-resume" when includeSession => await ToolRunner.RunAsync(log, "query", () => Task.FromResult(ToolOutcome.Fail("SESSION_RESUME_UNAVAILABLE", "The session resume reader is not configured.", "query(kind: \"campaign-resume\", id: \"...\")", "Campaign session resume was unavailable."))),
            "campaign-resume" => await new CampaignTools().ResumeAsync(campaignResumes, log, id ?? string.Empty, cancellationToken),
            "session-recap" when campaignSessionRecaps is not null => await new CampaignTools().SessionRecapAsync(campaignSessionRecaps, log, id ?? string.Empty, cancellationToken),
            "session-recap" => await ToolRunner.RunAsync(log, "query", () => Task.FromResult(ToolOutcome.Fail("SESSION_RECAP_UNAVAILABLE", "The session recap reader is not configured.", "query(kind: \"session-recap\", id: \"session....\")", "Session factual recap was unavailable."))),
            "quest-summary" => await new QuestTools().SummaryAsync(questSummaries, log, id ?? string.Empty, cancellationToken),
            "knowledge-answer" when knowledgeAnswers is not null => await new KnowledgeTools().AnswerAsync(
                knowledgeAnswers, log,
                new AuthorizedKnowledgeAnswerRequest(campaignId ?? string.Empty, question ?? string.Empty, knowledgeKinds, knowledgeSubjectIds, asOfMinute),
                cancellationToken),
            "knowledge-answer" => await ToolRunner.RunAsync(log, "query", () => Task.FromResult(ToolOutcome.Fail("KNOWLEDGE_AUDIENCE_UNAVAILABLE", "Knowledge answers require an explicitly enabled local development audience or a future authentication provider.", "Enable the documented local development audience, then retry.", "Knowledge audience was unavailable."))),
            "mechanics" =>
                await new MechanicTools().FindMechanicsAsync(
                    mechanics, log, id, version, query, category, scope, includeInactive, limit, cancellationToken),
            "event-types" => await new EventTypeTools().FindAsync(eventTypes, log, id, version, query, category, scope, includeInactive, limit, cancellationToken),
            "events" => await new EventTools().FindAsync(
                events, log, id, correlationId, causationId, rootOperationId, type, entityId,
                afterSequence, from, to, limit, cancellationToken),
            "subscriptions" => await new SubscriptionTools().FindAsync(subscriptions, log, id, version, query, category, scope, includeInactive, limit, cancellationToken),
            "notifications" => await new NotificationTools().FindAsync(
                notifications, log, id, state, topic, entityId, correlationId, from, to, limit, cancellationToken),
            "feedback" when feedback is not null => await new SystemFeedbackTools().FindAsync(
                feedback, log, id, category, impact, state, from, to, limit, cancellationToken),
            "feedback" => await ToolRunner.RunAsync(log, "query", () => Task.FromResult(ToolOutcome.Fail("FEEDBACK_UNAVAILABLE", "Feedback reporting is not configured.", "orient()", "Feedback query was unavailable."))),
            "history" =>
                await new HistoryTool().HistoryAsync(
                    log, limit ?? 20, failuresOnly, tool, subject, cancellationToken),
            _ => throw new InvalidOperationException($"Unhandled query kind '{kind}'.")
        };
    }

    private static bool IsQuestSummaryRequest(
        string? id, string[]? ids, int? version, string? query, string? nameQuery, string? withDefinitionId,
        string? category, string? scope, bool includeInactive, int? limit, int? sample, string[]? componentIds,
        int? containmentDepth, string[]? relationshipKinds, int? relationshipDepth, int? maxNodes, int? maxEdges,
        string? worldId, string? travellerId, string? destinationId, string? destinationLocationId,
        string? groundConveyanceId, string? aerialConveyanceId, bool failuresOnly, string? tool, string? subject,
        string? correlationId, string? causationId, string? rootOperationId, string? type, string? entityId,
        int? afterSequence, string? from, string? to, string? state, string? topic) =>
        id is { Length: > 6 } && id == id.Trim() && id.StartsWith("quest.", StringComparison.Ordinal) &&
        id.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-') &&
        ids is null && version is null && query is null && nameQuery is null && withDefinitionId is null &&
        category is null && scope is null && !includeInactive && limit is null && sample is null && componentIds is null &&
        containmentDepth is null && relationshipKinds is null && relationshipDepth is null && maxNodes is null &&
        maxEdges is null && worldId is null && travellerId is null && destinationId is null &&
        destinationLocationId is null && groundConveyanceId is null && aerialConveyanceId is null && !failuresOnly &&
        tool is null && subject is null && correlationId is null && causationId is null && rootOperationId is null &&
        type is null && entityId is null && afterSequence is null && from is null && to is null && state is null && topic is null;

    private static bool IsSessionRecapRequest(
        string? id, string[]? ids, int? version, string? query, string? nameQuery, string? withDefinitionId,
        string? category, string? scope, bool includeInactive, int? limit, int? sample, string[]? componentIds,
        int? containmentDepth, string[]? relationshipKinds, int? relationshipDepth, int? maxNodes, int? maxEdges,
        string? worldId, string? travellerId, string? destinationId, string? destinationLocationId,
        string? groundConveyanceId, string? aerialConveyanceId, bool failuresOnly, string? tool, string? subject,
        string? correlationId, string? causationId, string? rootOperationId, string? type, string? entityId,
        int? afterSequence, string? from, string? to, string? state, string? topic, bool includeSession) =>
        id is { Length: > 8 } && id == id.Trim() && id.StartsWith("session.", StringComparison.Ordinal) &&
        id.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-') &&
        ids is null && version is null && query is null && nameQuery is null && withDefinitionId is null &&
        category is null && scope is null && !includeInactive && limit is null && sample is null && componentIds is null &&
        containmentDepth is null && relationshipKinds is null && relationshipDepth is null && maxNodes is null &&
        maxEdges is null && worldId is null && travellerId is null && destinationId is null &&
        destinationLocationId is null && groundConveyanceId is null && aerialConveyanceId is null && !failuresOnly &&
        tool is null && subject is null && correlationId is null && causationId is null && rootOperationId is null &&
        type is null && entityId is null && afterSequence is null && from is null && to is null && state is null && topic is null && !includeSession;
}
