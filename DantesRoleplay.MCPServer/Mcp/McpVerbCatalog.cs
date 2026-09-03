using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.Capabilities;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>Ruleset-neutral catalog for the generic three-verb protocol.</summary>
public static class McpVerbCatalog
{
    private static readonly JsonSerializerOptions Readable = new() { WriteIndented = false };
    private const string DirectApplicationActionInputSchema = """
        {"type":"object","additionalProperties":false,"required":["payload"],"properties":{"payload":{"type":"object","additionalProperties":false,"required":["idempotencyKey","applicationId","stateSpaceId","qualifiedMechanicId","mechanicVersion","contentFingerprint","roleEntityIds","input"],"properties":{"idempotencyKey":{"type":"string","minLength":1,"maxLength":200},"applicationId":{"type":"string","minLength":1,"maxLength":63},"stateSpaceId":{"type":"string","minLength":1,"maxLength":200},"qualifiedMechanicId":{"type":"string","minLength":1,"maxLength":200},"mechanicVersion":{"type":"integer","minimum":1},"contentFingerprint":{"type":"string","pattern":"^[0-9A-F]{64}$"},"roleEntityIds":{"type":"object","maxProperties":32,"additionalProperties":{"type":"string","minLength":1,"maxLength":200}},"input":{"type":"object"}}},"intent":{"type":"string"},"proceduresUsed":{"type":"array","items":{"type":"string"}},"dryRun":{"type":"boolean","const":false}}}
        """;
    private const string DirectApplicationActionOutputSchema = """
        {"type":"object","additionalProperties":true,"required":["ok"],"properties":{"ok":{"type":"boolean"},"data":{"type":["object","null"],"additionalProperties":false,"required":["affectedEntityIds","narration","receipt","nextActions"],"properties":{"affectedEntityIds":{"type":"array","items":{"type":"string"}},"narration":{"type":"string"},"receipt":{"type":"object","additionalProperties":false,"required":["operationId","disposition","qualifiedMechanicId","mechanicVersion","contentFingerprint","seed","appliedEffectCount","effects"],"properties":{"operationId":{"type":"string"},"disposition":{"enum":["succeeded","replayed"]},"qualifiedMechanicId":{"type":"string"},"mechanicVersion":{"type":"integer","minimum":1},"contentFingerprint":{"type":"string","pattern":"^[0-9A-F]{64}$"},"seed":{"type":"integer"},"appliedEffectCount":{"type":"integer","minimum":0},"effects":{"type":"array","items":{"type":"object"}}}},"nextActions":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["id","description","capabilityId","capabilityFingerprint","inputSchemaHash","interface","tool","kind","requiredArguments","knownArguments","missingArguments","arguments","ready"],"properties":{"id":{"type":"string"},"description":{"type":"string"},"capabilityId":{"type":"string"},"capabilityFingerprint":{"type":"string","pattern":"^[0-9A-F]{64}$"},"inputSchemaHash":{"type":"string","pattern":"^[0-9A-F]{64}$"},"interface":{"const":"mcp"},"tool":{"enum":["query","commit"]},"kind":{"type":"string"},"requiredArguments":{"type":"array","items":{"type":"string"}},"knownArguments":{"type":"object"},"missingArguments":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["name","description"],"properties":{"name":{"type":"string"},"description":{"type":"string"}}}},"arguments":{"type":"object"},"ready":{"type":"boolean"}}}}}},"error":{"type":["object","null"]}}}
        """;
    public static IReadOnlyList<QueryKindSpec> QueryKinds { get; } =
    [
        new("capabilities", "The complete generic query and commit catalog.", [], []),
        new("procedures", "Versioned system procedure contracts.", ["id", "version", "query", "category", "includeInactive", "limit"], ["procedure.system.inspect"]),
        new("categories", "One category-tree branch for procedures or mechanics.", ["catalog", "category", "includeInactive"], ["procedure.system.hierarchical-catalogs"]),
        new("world", "Generic entity/component state and a bounded sample.", ["sample"], ["procedure.world.model"]),
        new("entities", "Generic entities by id, name, or component definition; with applicationId and stateSpaceId, exact entities from an application's live state space by id/ids, or a bounded name/component search with nameQuery or withDefinitionId.", ["id", "ids", "nameQuery", "withDefinitionId", "applicationId", "stateSpaceId", "limit"], ["procedure.system.inspect"]),
        new("graph", "A bounded graph over generic components, containment, and relationships.", ["id", "componentIds", "containmentDepth", "relationshipKinds", "relationshipDepth", "maxNodes", "maxEdges"], ["procedure.system.inspect"]),
        new("mechanics", "Versioned JavaScript mechanics and their declared contracts.", ["id", "version", "query", "category", "scope", "includeInactive", "limit"], ["procedure.mechanic.find"]),
        new("event-types", "Versioned structural event schemas.", ["id", "version", "query", "category", "scope", "includeInactive", "limit"], ["procedure.event.define"]),
        new("events", "The structural event ledger.", ["id", "correlationId", "causationId", "rootOperationId", "type", "entityId", "afterSequence", "from", "to", "limit"], ["procedure.event.inspect"]),
        new("subscriptions", "Generic guard/reaction middleware registrations.", ["id", "version", "query", "category", "scope", "includeInactive", "limit"], ["procedure.subscription.create"]),
        new("notifications", "System notifications.", ["id", "state", "topic", "entityId", "correlationId", "from", "to", "limit"], ["procedure.notification.inspect"]),
        new("feedback", "Append-only feedback about the system.", ["id", "category", "impact", "state", "from", "to", "limit"], ["procedure.system.feedback"]),
        new("information-answer", "A bounded answer over an authorized generic information scope.", ["scopeId", "question", "sourceIds", "limit"], ["procedure.information.answer"]),
        new("information-actions", "Declared actions in an authorized generic information scope.", ["scopeId"], ["procedure.information.action"]),
        new("system.audience-context", "The current host-authorized application, state-space, scope, and actor binding. It accepts no caller-selected identity.", [], ["procedure.system.inspect"]),
        new("system.applications", "Authenticated bounded inspection of registered applications and revisions.", ["applicationId", "limit"], ["procedure.system.inspect"]),
        new("system.sources", "Authenticated bounded inspection of registered relative sources and latest scan evidence.", ["applicationId", "id", "limit"], ["procedure.system.inspect"]),
        new("system.application-preview", "Authenticated disposable scan and candidate resolution preview for reviewed base sources plus registered extensions.", ["applicationId", "sourceIds", "extensionIds", "limit"], ["procedure.system.inspect"]),
        new("system.dependencies", "Authenticated declared component-field and projection dependency impact for one application.", ["applicationId", "id", "transitive", "limit"], ["procedure.system.inspect"]),
        new("system.catalogs", "Public catalog collections for one application.", ["applicationId"], ["procedure.system.inspect"]),
        new("system.catalog.browse", "One described public catalog branch with bounded cursor paging.", ["applicationId", "collection", "branch", "pageSize", "cursor"], ["procedure.system.inspect"]),
        new("system.catalog.search", "Description-aware deterministic lexical search over the automatically resolved active application catalog.", ["applicationId", "query", "collection", "branch", "kinds", "statuses", "namespaceId", "includeShadowed", "pageSize", "cursor"], ["procedure.system.inspect"]),
        new("system.catalog.record", "One exact public effective catalog record with provenance.", ["applicationId", "collection", "id"], ["procedure.system.inspect"]),
        new("system.feature-search", "Description-aware intent search over automatically resolved trusted application mechanics and procedures.", ["applicationId", "query", "id", "namespaceId", "limit"], ["procedure.system.inspect"]),
        new("system.interaction-plan", "Resolve an inert, exact-contract interaction proposal or verify a caller-submitted proposal.", ["applicationId", "request"], ["procedure.system.use"]),
        new("system.interaction-receipt", "Read one authorized resolution or execution receipt.", ["applicationId", "stateSpaceId", "id"], ["procedure.system.inspect"]),
        new("system.interaction-recipes", "Privately inspect learned route candidates or search verified routes for one application.", ["applicationId", "id", "query", "status", "cursor", "limit"], ["procedure.system.inspect"]),
        new("system.trigger-scheduling", "Privately inspect current schedules, observations, past fires, phone devices, sources, and structures.", ["applicationId", "resource", "id", "limit"], ["procedure.system.inspect"]),
        new("system.blobs", "Private metadata and transfer locations for one finalized content-addressed image blob.", ["id"], ["procedure.system.inspect"]),
        new("namespaces", "The registered catalog namespaces every authored identity is placed in. Omit id to list or search, pass id for one.", ["id", "query", "includeInactive", "limit"], ["procedure.system.namespace"]),
        new("history", "Recent audited operations.", ["limit", "failuresOnly", "tool", "subject"], ["procedure.system.inspect"])
    ];

    public static IReadOnlyList<CommitKindSpec> CommitKinds { get; } =
    [
        new("application.action.execute", "Execute one already selected exact application mechanic in one authorized, confirmed, idempotent call.", "{\"idempotencyKey\":\"application-action.example.1\",\"applicationId\":\"example\",\"stateSpaceId\":\"example-space\",\"qualifiedMechanicId\":\"example.mechanic.fixture\",\"mechanicVersion\":1,\"contentFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"roleEntityIds\":{},\"input\":{}}", false, ["procedure.system.use"], DirectApplicationActionInputSchema, DirectApplicationActionOutputSchema),
        new("feedback", "Append one immutable system problem, friction report, documentation gap, suggestion, or positive observation.", "{\"operation\":\"submit\",\"requestToken\":\"feedback-request.0123456789abcdef0123456789abcdef\",\"category\":\"defect\",\"impact\":\"degraded\",\"summary\":\"What failed\",\"observed\":\"What the caller observed\"}", false, ["procedure.system.feedback"]),
        new("system.application.register", "Register immutable application metadata with authenticated replay protection.", "{\"requestToken\":\"0123456789abcdef0123456789abcdef\",\"applicationId\":\"example\",\"displayName\":\"Example\",\"description\":\"Example application.\",\"baseApplications\":[],\"expectedFingerprint\":null}", true, ["procedure.system.use"]),
        new("system.source.register", "Register an immutable allowed-root-relative source with authenticated replay protection.", "{\"requestToken\":\"0123456789abcdef0123456789abcdee\",\"applicationId\":\"example\",\"sourceId\":\"core\",\"allowedRootId\":\"workspace\",\"relativePathOrGlob\":\"catalog/**/*.json\",\"trust\":\"trusted\",\"precedence\":0,\"logicalIdentity\":\"catalog\",\"expectedFingerprint\":null}", true, ["procedure.system.use"]),
        new("system.extension.register", "Register immutable application-extension membership, namespaces, classification, and precedence.", "{\"requestToken\":\"0123456789abcdef0123456789abcded\",\"applicationId\":\"example\",\"extensionId\":\"homebrew\",\"displayName\":\"Example Homebrew\",\"description\":\"Reviewed homebrew extension.\",\"classification\":\"homebrew\",\"sourceIds\":[\"homebrew\"],\"namespaceIds\":[\"example.extension.homebrew\"],\"dependencies\":[],\"conflictsWith\":[],\"higherPriorityThan\":[],\"overridesBase\":true,\"expectedFingerprint\":null}", true, ["procedure.system.use"]),
        new("system.component-type.register", "Register one immutable versioned application component-type schema.", "{\"requestToken\":\"0123456789abcdef0123456789abcdff\",\"applicationId\":\"example\",\"qualifiedTypeId\":\"example.note\",\"schemaJson\":\"{\\\"type\\\":\\\"object\\\"}\",\"expectedSchemaHash\":null}", true, ["procedure.system.use"]),
        new("system.application.activate", "Activate an exact valid deterministic reviewed base-source and extension set with authenticated replay protection.", "{\"requestToken\":\"0123456789abcdef0123456789abcded\",\"applicationId\":\"example\",\"previewFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"expectedActiveFingerprint\":null,\"sourceIds\":[\"core\"],\"extensionIds\":[\"homebrew\"]}", true, ["procedure.system.use"]),
        new("system.state-space.create", "Create one empty runtime or application-publication state space bound to an exact active application fingerprint. Scope defaults to runtime-state-space when omitted.", "{\"requestToken\":\"0123456789abcdef0123456789abcdec\",\"stateSpaceId\":\"example-space\",\"applicationId\":\"example\",\"scope\":\"runtime-state-space\",\"activeFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"expectedFingerprint\":null}", true, ["procedure.system.use"]),
        new("system.state-space.upgrade", "Rebind a state space to the exact current activation only after retained compatibility evidence succeeds.", "{\"requestToken\":\"0123456789abcdef0123456789abcdeb\",\"stateSpaceId\":\"example-space\",\"applicationId\":\"example\",\"activeFingerprint\":\"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\",\"expectedBindingFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}", true, ["procedure.system.use"]),
        new("system.state-space.adopt-legacy", "Copy either the complete legacy ECS graph or an explicitly closed entity scope into one new application state space using exact type and relationship mappings.", "{\"requestToken\":\"0123456789abcdef0123456789abcbea\",\"stateSpaceId\":\"example-space\",\"applicationId\":\"example\",\"activeFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"componentMappings\":[],\"relationshipMappings\":[],\"entityIds\":[\"entity.example\"]}", true, ["procedure.system.use"]),
        new("system.world-state.sync", "Preview or atomically apply one reviewed additive/update-only manifest beneath an existing application World root.", "{\"requestToken\":\"0123456789abcdef0123456789abcbe9\",\"applicationId\":\"example\",\"stateSpaceId\":\"example-space\",\"rootEntityId\":\"world.example\",\"entities\":[{\"entityId\":\"location.example\",\"name\":\"Example\",\"expectedRevision\":0,\"components\":[],\"containment\":{\"containerEntityId\":\"world.example\",\"slot\":\"location\",\"expectedRevision\":0}}],\"relationships\":[]}", true, ["procedure.system.use"]),
        new("system.interaction-execute", "Execute a previously resolved exact query/action proposal with explicit consent, typed query-result bindings, safe query receipts, and deterministic replay protection. Learning is separately opt-in.", "{\"applicationId\":\"example\",\"stateSpaceId\":\"example-space\",\"resolutionReceiptId\":\"receipt.example\",\"proposalFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"idempotencyKey\":\"execute.example\",\"proposal\":{\"command\":\"propose\",\"steps\":[{\"stepId\":\"step.1\",\"kind\":\"action\",\"qualifiedId\":\"example.mechanic.fixture\",\"version\":1,\"fingerprint\":\"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB\",\"dependsOn\":[],\"roleBindings\":{},\"input\":{},\"resultBindings\":[]}]},\"stopOnFailure\":true,\"learn\":false}", false, ["procedure.system.use"]),
        new("system.interaction-recipe-review", "Verify or retire one learned route through private, replay-protected review.", "{\"requestToken\":\"review.example.1\",\"applicationId\":\"example\",\"recipeId\":\"example.recipe.0123456789abcdef0123456789abcdef\",\"expectedVersion\":1,\"decision\":\"verify\",\"reason\":\"Reviewed against current contracts.\"}", false, ["procedure.system.use"]),
        new("system.trigger-scheduling", "Preview or apply one closed trigger-scheduling administration command.", "{\"requestToken\":\"0123456789abcdef0123456789abcdef\",\"operation\":\"one-time.register\",\"applicationId\":\"example\",\"value\":{\"id\":\"example.reminder\",\"version\":1,\"dueAtUtc\":\"2026-08-25T23:00:00Z\",\"misfirePolicy\":\"fire-once\",\"lifecycle\":\"active\",\"notification\":{\"topic\":\"scheduled.reminder\",\"subject\":\"Time to stop\",\"body\":\"Softly end the session.\",\"stateSpaceId\":null,\"entityIds\":[]}}}", true, ["procedure.system.use"]),
        new("system.knowledge-state.sync", "Preview or atomically apply one exact reviewed actor knowledge-state manifest for the ambient private player seat.", "{\"requestToken\":\"knowledge.sync.example.1\",\"campaignId\":\"campaign.example\",\"entries\":[{\"knowledgeId\":\"fact.example\",\"state\":\"known\"}]}", true, ["procedure.game.core.world.knowledge"]),
        new("system.namespace.register", "Register one new catalog namespace so identities may be authored beneath it. It arrives needing review; nothing may be written into it until a person reviews it.", "{\"id\":\"mechanic.example.thing\",\"owner\":\"example\",\"description\":\"Mechanics for the example thing.\",\"allowedKinds\":[\"mechanic\"]}", true, ["procedure.system.namespace"]),
        new("system.blob-upload.begin", "Create a private, short-lived, one-use HTTP capability for a declared image blob.", "{\"sha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"mediaType\":\"image/png\",\"byteLength\":1024}", false, ["procedure.system.use"]),
        new("system.blob-upload.finalize", "Verify and publish bytes already transferred through a blob upload capability.", "{\"uploadId\":\"blob-upload.0123456789abcdef0123456789abcdef\",\"uploadToken\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"}", false, ["procedure.system.use"])
    ];

    public static IReadOnlyList<string> QueryKindNames => QueryKinds.Select(kind => kind.Name).ToArray();
    public static IReadOnlyList<string> CommitKindNames => CommitKinds.Select(kind => kind.Name).ToArray();
    public static IReadOnlyList<CapabilityContractDescriptor> Descriptors { get; } =
        QueryKinds.Select(value => value.Descriptor)
            .Concat(CommitKinds.Select(value => value.Descriptor))
            .OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();
    public static bool IsQueryKind(string kind) => QueryKinds.Any(item => item.Name == kind);
    public static CommitKindSpec? Commit(string kind) => CommitKinds.FirstOrDefault(item => item.Name == kind);
    internal static CommitKindSpec? DispatchCommit(string kind) => Commit(kind);

    public static string CommitCall(string kind, bool dryRun = false)
    {
        var spec = Commit(kind);
        if (spec is null) return "query(kind: \"capabilities\")";
        var payload = JsonNode.Parse(spec.Descriptor.Examples[0].InputJson)?["payload"]
            ?.ToJsonString(Readable) ?? "{}";
        return dryRun && spec.Descriptor.Operations.SupportsPreview
            ? $"commit(kind: \"{kind}\", payload: {JsonSerializer.Serialize(payload)}, dryRun: true)"
            : $"commit(kind: \"{kind}\", payload: {JsonSerializer.Serialize(payload)})";
    }

    public static string CommitCall(string kind, string id, bool dryRun = false)
    {
        var spec = Commit(kind);
        if (spec is null) return "query(kind: \"capabilities\")";
        var example = JsonNode.Parse(spec.Descriptor.Examples[0].InputJson)?["payload"]?.DeepClone();
        if (example is JsonObject objectExample) objectExample["id"] = id;
        var payload = example?.ToJsonString(Readable) ?? "{}";
        return dryRun && spec.Descriptor.Operations.SupportsPreview
            ? $"commit(kind: \"{kind}\", payload: {JsonSerializer.Serialize(payload)}, dryRun: true)"
            : $"commit(kind: \"{kind}\", payload: {JsonSerializer.Serialize(payload)})";
    }

    public static object Catalog() => new
    {
        Capabilities = Descriptors,
        NothingElseExists = "Only these generic kinds are available in this host."
    };
}

public sealed record QueryKindSpec
{
    public QueryKindSpec(string name, string description, IReadOnlyList<string> inputNames,
        IReadOnlyList<string> procedureIds)
    {
        Name = name;
        Descriptor = McpCapabilityContractAdapter.Query(name, description, inputNames, procedureIds);
    }

    public string Name { get; }
    public CapabilityContractDescriptor Descriptor { get; }
}

public sealed record CommitKindSpec
{
    public CommitKindSpec(string name, string summary, string example,
        bool supportsDryRun, IReadOnlyList<string> contracts, string? inputSchemaJson = null,
        string? outputSchemaJson = null, string lifecycle = CapabilityContractLifecycle.Active,
        string? replacementCapabilityId = null)
    {
        Name = name;
        Descriptor = McpCapabilityContractAdapter.Commit(name, summary, example, supportsDryRun,
            contracts, inputSchemaJson, outputSchemaJson, lifecycle, replacementCapabilityId);
    }

    public string Name { get; }
    public CapabilityContractDescriptor Descriptor { get; }
}
