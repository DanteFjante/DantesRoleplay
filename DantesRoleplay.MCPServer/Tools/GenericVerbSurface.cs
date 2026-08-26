using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Effects;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>Ruleset-neutral catalog for the generic three-verb protocol.</summary>
public static class VerbSurface
{
    private static readonly JsonSerializerOptions Readable = new() { WriteIndented = false };

    public static IReadOnlyList<QueryKindSpec> QueryKinds { get; } =
    [
        new("capabilities", "The complete generic query and commit catalog.", [], []),
        new("procedures", "Versioned system procedure contracts.", ["id", "version", "query", "category", "includeInactive", "limit"], ["procedure.system.inspect"]),
        new("categories", "One category-tree branch for procedures or mechanics.", ["catalog", "category", "includeInactive"], ["procedure.system.hierarchical-catalogs"]),
        new("world", "Generic entity/component state and a bounded sample.", ["sample"], ["procedure.world.model"]),
        new("entities", "Generic entities by id, name, or component definition.", ["id", "ids", "nameQuery", "withDefinitionId", "limit"], ["procedure.system.inspect"]),
        new("graph", "A bounded graph over generic components, containment, and relationships.", ["id", "componentIds", "containmentDepth", "relationshipKinds", "relationshipDepth", "maxNodes", "maxEdges"], ["procedure.system.inspect"]),
        new("mechanics", "Versioned JavaScript mechanics and their declared contracts.", ["id", "version", "query", "category", "scope", "includeInactive", "limit"], ["procedure.mechanic.find"]),
        new("event-types", "Versioned structural event schemas.", ["id", "version", "query", "category", "scope", "includeInactive", "limit"], ["procedure.event.define"]),
        new("events", "The structural event ledger.", ["id", "correlationId", "causationId", "rootOperationId", "type", "entityId", "afterSequence", "from", "to", "limit"], ["procedure.event.inspect"]),
        new("subscriptions", "Generic guard/reaction middleware registrations.", ["id", "version", "query", "category", "scope", "includeInactive", "limit"], ["procedure.subscription.create"]),
        new("notifications", "System notifications.", ["id", "state", "topic", "entityId", "correlationId", "from", "to", "limit"], ["procedure.notification.inspect"]),
        new("feedback", "Append-only feedback about the system.", ["id", "category", "impact", "state", "from", "to", "limit"], ["procedure.system.feedback"]),
        new("information-answer", "A bounded answer over an authorized generic information scope.", ["scopeId", "question", "sourceIds", "limit"], ["procedure.information.answer"]),
        new("information-actions", "Declared actions in an authorized generic information scope.", ["scopeId"], ["procedure.information.action"]),
        new("system.applications", "Authenticated bounded inspection of registered applications and revisions.", ["applicationId", "limit"], ["procedure.system.inspect"]),
        new("system.sources", "Authenticated bounded inspection of registered relative sources and latest scan evidence.", ["applicationId", "id", "limit"], ["procedure.system.inspect"]),
        new("system.application-preview", "Authenticated disposable scan and candidate overlay preview for one exact registered-source profile.", ["applicationId", "sourceIds", "limit"], ["procedure.system.inspect"]),
        new("system.dependencies", "Authenticated declared component-field and projection dependency impact for one application.", ["applicationId", "id", "transitive", "limit"], ["procedure.system.inspect"]),
        new("system.catalogs", "Public catalog collections for one application.", ["applicationId"], ["procedure.system.inspect"]),
        new("system.catalog.browse", "One described public catalog branch with bounded cursor paging.", ["applicationId", "collection", "branch", "pageSize", "cursor"], ["procedure.system.inspect"]),
        new("system.catalog.search", "Deterministic lexical search over one public application catalog.", ["applicationId", "query", "collection", "branch", "kinds", "statuses", "pageSize", "cursor"], ["procedure.system.inspect"]),
        new("system.catalog.record", "One exact public effective catalog record with provenance.", ["applicationId", "collection", "id"], ["procedure.system.inspect"]),
        new("system.feature-search", "Intent-oriented search over current trusted application mechanics and procedures.", ["applicationId", "query", "id", "limit"], ["procedure.system.inspect"]),
        new("system.interaction-plan", "Resolve an inert, exact-contract interaction proposal or verify a caller-submitted proposal.", ["applicationId", "request"], ["procedure.system.use"]),
        new("system.interaction-receipt", "Read one authorized resolution or execution receipt.", ["applicationId", "stateSpaceId", "id"], ["procedure.system.inspect"]),
        new("system.interaction-recipes", "Privately inspect learned route candidates or search verified routes for one application.", ["applicationId", "id", "query", "status", "cursor", "limit"], ["procedure.system.inspect"]),
        new("system.trigger-scheduling", "Privately inspect current schedules, observations, past fires, phone devices, sources, and structures.", ["applicationId", "resource", "id", "limit"], ["procedure.system.inspect"]),
        new("history", "Recent audited operations.", ["limit", "failuresOnly", "tool", "subject"], ["procedure.system.inspect"])
    ];

    public static IReadOnlyList<CommitKindSpec> CommitKinds { get; } =
    [
        new("component", "Declare or revise a generic component definition.", "{id, name, description, schema?}", "{\"id\":\"...\",\"name\":\"...\",\"description\":\"...\",\"schema\":\"{}\"}", false, ["procedure.world.model"]),
        new("effects", "Apply a validated list of generic world effects atomically.", "{effects:[...]}", "{\"effects\":[{\"type\":\"entity.create\",\"entityId\":\"...\",\"name\":\"...\"}]}", true, ["procedure.world.change"]),
        new("mechanic", "Write or revise a generic JavaScript mechanic.", "{id, category, name, source, description?, matches?, requirements?, scope?, status?, changeNote?}", "{\"id\":\"mechanic.example\",\"category\":\"example\",\"name\":\"Example\",\"source\":\"return { effects: [] };\"}", true, ["procedure.mechanic.write"]),
        new("action", "Run a selected JavaScript mechanic through the generic action pipeline.", "{intent, roleEntityIds?, input?, scope?, seed?}", "{\"intent\":\"what the actor tries to do\",\"roleEntityIds\":{},\"input\":\"{}\"}", false, ["procedure.action.run"]),
        new("system.application.register", "Register immutable application metadata with authenticated replay protection.", "{requestToken, applicationId, displayName, description, baseApplications, expectedFingerprint}", "{\"requestToken\":\"0123456789abcdef0123456789abcdef\",\"applicationId\":\"example\",\"displayName\":\"Example\",\"description\":\"Example application.\",\"baseApplications\":[],\"expectedFingerprint\":null}", true, ["procedure.system.use"]),
        new("system.source.register", "Register an immutable allowed-root-relative source with authenticated replay protection.", "{requestToken, applicationId, sourceId, allowedRootId, relativePathOrGlob, trust, precedence, logicalIdentity, expectedFingerprint}", "{\"requestToken\":\"0123456789abcdef0123456789abcdee\",\"applicationId\":\"example\",\"sourceId\":\"core\",\"allowedRootId\":\"workspace\",\"relativePathOrGlob\":\"catalog/**/*.json\",\"trust\":\"trusted\",\"precedence\":0,\"logicalIdentity\":\"catalog\",\"expectedFingerprint\":null}", true, ["procedure.system.use"]),
        new("system.component-type.register", "Register one immutable versioned application component-type schema.", "{requestToken, applicationId, qualifiedTypeId, schemaJson, expectedSchemaHash}", "{\"requestToken\":\"0123456789abcdef0123456789abcdff\",\"applicationId\":\"example\",\"qualifiedTypeId\":\"example.note\",\"schemaJson\":\"{\\\"type\\\":\\\"object\\\"}\",\"expectedSchemaHash\":null}", true, ["procedure.system.use"]),
        new("system.application.activate", "Activate an exact valid registered-source profile with authenticated replay protection.", "{requestToken, applicationId, previewFingerprint, expectedActiveFingerprint, sourceIds?}", "{\"requestToken\":\"0123456789abcdef0123456789abcded\",\"applicationId\":\"example\",\"previewFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"expectedActiveFingerprint\":null,\"sourceIds\":[\"core\"]}", true, ["procedure.system.use"]),
        new("system.state-space.create", "Create one empty isolated state space bound to an exact active application fingerprint.", "{requestToken, stateSpaceId, applicationId, activeFingerprint, expectedFingerprint}", "{\"requestToken\":\"0123456789abcdef0123456789abcdec\",\"stateSpaceId\":\"example-space\",\"applicationId\":\"example\",\"activeFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"expectedFingerprint\":null}", true, ["procedure.system.use"]),
        new("system.state-space.upgrade", "Upgrade an empty state space to the exact current activation with retained compatibility evidence.", "{requestToken, stateSpaceId, applicationId, activeFingerprint, expectedBindingFingerprint}", "{\"requestToken\":\"0123456789abcdef0123456789abcdeb\",\"stateSpaceId\":\"example-space\",\"applicationId\":\"example\",\"activeFingerprint\":\"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\",\"expectedBindingFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}", true, ["procedure.system.use"]),
        new("system.state-space.adopt-legacy", "Copy the complete legacy ECS graph into one new application state space using explicit exact type and relationship mappings.", "{requestToken, stateSpaceId, applicationId, activeFingerprint, componentMappings, relationshipMappings}", "{\"requestToken\":\"0123456789abcdef0123456789abcbea\",\"stateSpaceId\":\"example-space\",\"applicationId\":\"example\",\"activeFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"componentMappings\":[],\"relationshipMappings\":[]}", true, ["procedure.system.use"]),
        new("system.interaction-execute", "Execute a previously resolved exact query/action proposal with explicit consent, typed query-result bindings, safe query receipts, and deterministic replay protection. Learning is separately opt-in.", "{applicationId, stateSpaceId, resolutionReceiptId, proposalFingerprint, idempotencyKey, proposal, stopOnFailure?, learn?, learningIntent?}", "{\"applicationId\":\"example\",\"stateSpaceId\":\"example-space\",\"resolutionReceiptId\":\"receipt.example\",\"proposalFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"idempotencyKey\":\"execute.example\",\"proposal\":{\"command\":\"propose\",\"steps\":[{\"stepId\":\"step.1\",\"kind\":\"action\",\"qualifiedId\":\"example.mechanic.fixture\",\"version\":1,\"fingerprint\":\"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\",\"dependsOn\":[],\"roleBindings\":{},\"input\":{},\"resultBindings\":[]}]},\"stopOnFailure\":true,\"learn\":false}", false, ["procedure.system.use"]),
        new("system.interaction-recipe-review", "Verify or retire one learned route through private, replay-protected review.", "{requestToken, applicationId, recipeId, expectedVersion, decision, reason}", "{\"requestToken\":\"review.example.1\",\"applicationId\":\"example\",\"recipeId\":\"example.recipe.0123456789abcdef0123456789abcdef\",\"expectedVersion\":1,\"decision\":\"verify\",\"reason\":\"Reviewed against current contracts.\"}", false, ["procedure.system.use"]),
        new("system.trigger-scheduling", "Preview or apply one closed trigger-scheduling administration command.", "{requestToken, operation, applicationId, value}", "{\"requestToken\":\"0123456789abcdef0123456789abcdef\",\"operation\":\"one-time.register\",\"applicationId\":\"example\",\"value\":{\"id\":\"example.reminder\",\"version\":1,\"dueAtUtc\":\"2026-08-25T23:00:00Z\",\"misfirePolicy\":\"fire-once\",\"lifecycle\":\"active\",\"notification\":{\"topic\":\"scheduled.reminder\",\"subject\":\"Time to stop\",\"body\":\"Softly end the session.\",\"stateSpaceId\":null,\"entityIds\":[]}}}", true, ["procedure.system.use"])
    ];

    public static IReadOnlyDictionary<string, string> QueryParameters { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["id"] = "One full record.", ["ids"] = "Several entities in full.", ["version"] = "A historical version.",
        ["query"] = "Search text.", ["category"] = "Category branch.", ["scope"] = "Ruleset/application scope.",
        ["limit"] = "Maximum records returned.", ["sample"] = "World example count.", ["scopeId"] = "Authorized information scope.",
        ["sourceIds"] = "Exact source filter for supported information or application-profile queries.", ["applicationId"] = "One registered non-system application.",
        ["collection"] = "One application-declared catalog collection.", ["branch"] = "A slash-separated logical catalog branch.",
        ["pageSize"] = "Catalog page size from 1 through 100.", ["cursor"] = "Authenticated catalog continuation cursor.",
        ["kinds"] = "Catalog record-kind filters.", ["statuses"] = "Catalog record-status filters.",
        ["failuresOnly"] = "Only failed audit records.", ["tool"] = "Audit tool filter.", ["subject"] = "Audit subject filter.",
        ["transitive"] = "Dependency impact: include indirect declared dependents; default true.",
        ["stateSpaceId"] = "One application-bound state space.",
        ["request"] = "A closed interaction request encoded as JSON.",
        ["status"] = "A closed recipe status filter.",
        ["resource"] = "A closed trigger-scheduling projection name."
    };

    public static IReadOnlyDictionary<string, string> EffectVocabulary { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [EffectType.EntityCreate] = "entityId, name.",
        [EffectType.EntityDelete] = "entityId.",
        [EffectType.ComponentAdd] = "entityId, definitionId, data.",
        [EffectType.ComponentSet] = "entityId, definitionId, data.",
        [EffectType.ComponentMerge] = "entityId, definitionId, data.",
        [EffectType.ComponentRemove] = "entityId, definitionId.",
        [EffectType.ContainmentMove] = "entityId, toEntityId?, slot?.",
        [EffectType.RelationshipCreate] = "entityId, toEntityId, kind, data.",
        [EffectType.RelationshipRemove] = "entityId, toEntityId, kind."
    };

    public static IReadOnlyList<string> QueryKindNames => QueryKinds.Select(kind => kind.Name).ToArray();
    public static IReadOnlyList<string> CommitKindNames => CommitKinds.Select(kind => kind.Name).ToArray();
    public static bool IsQueryKind(string kind) => QueryKinds.Any(item => item.Name == kind);
    public static CommitKindSpec? Commit(string kind) => CommitKinds.FirstOrDefault(item => item.Name == kind);

    public static string CommitCall(string kind, bool dryRun = false)
    {
        var spec = Commit(kind);
        if (spec is null) return "query(kind: \"capabilities\")";
        var payload = JsonNode.Parse(spec.Example)?.ToJsonString(Readable) ?? "{}";
        return dryRun && spec.SupportsDryRun
            ? $"commit(kind: \"{kind}\", payload: {JsonSerializer.Serialize(payload)}, dryRun: true)"
            : $"commit(kind: \"{kind}\", payload: {JsonSerializer.Serialize(payload)})";
    }

    public static string CommitCall(string kind, string id, bool dryRun = false)
    {
        var spec = Commit(kind);
        if (spec is null) return "query(kind: \"capabilities\")";
        var example = JsonNode.Parse(spec.Example);
        if (example is JsonObject objectExample) objectExample["id"] = id;
        var payload = example?.ToJsonString(Readable) ?? "{}";
        return dryRun && spec.SupportsDryRun
            ? $"commit(kind: \"{kind}\", payload: {JsonSerializer.Serialize(payload)}, dryRun: true)"
            : $"commit(kind: \"{kind}\", payload: {JsonSerializer.Serialize(payload)})";
    }

    public static object Announcement() => new
    {
        Orient = "orient — describe this generic system.",
        Query = QueryKinds.ToDictionary(item => item.Name, item => item.Returns),
        Commit = CommitKinds.ToDictionary(item => item.Name, item => item.Summary)
    };

    public static object Catalog() => new
    {
        Query = QueryKinds,
        Commit = CommitKinds,
        QueryParameters,
        NothingElseExists = "Only these generic kinds are available in this host."
    };
}

public sealed record QueryKindSpec(string Name, string Returns, IReadOnlyList<string> Reads, IReadOnlyList<string> Contracts);
public sealed record CommitKindSpec(string Name, string Summary, string Payload, string Example, bool SupportsDryRun, IReadOnlyList<string> Contracts);
