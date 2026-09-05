using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Media;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Knowledge;

/// <summary>
/// Read-only observer materialization. The application supplies vocabulary; effective knowledge
/// and participation retain their existing owners. No caller can supply a trusted context.
/// </summary>
public sealed class ApplicationAuthorizedProjectionResolver(
    DantesRoleplayDbContext db,
    IAuthorizedKnowledgeAudiencePolicy audiences,
    IKnowledgeApplicationBindingResolver bindings,
    IKnowledgeActorParticipationVerifier participation,
    IKnowledgeCanonicalSource source,
    IKnowledgeEffectiveStateResolver states,
    IEntityMediaService? media = null,
    IReadModelMediaLinkStore? mediaLinks = null) : IApplicationAuthorizedProjectionResolver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly byte[] RevisionKey = RandomNumberGenerator.GetBytes(32);

    public async Task<ProjectionResult> ResolveAsync(ApplicationMechanicEvaluationRequest request,
        MechanicRequirements requirements, CancellationToken cancellationToken = default)
    {
        try
        {
            if (request.InputJson is null || Encoding.UTF8.GetByteCount(request.InputJson) > 1024)
                return ProjectionResult.Failed("READ_MODEL_INPUT_INVALID");
            var declared = requirements.AuthorizedContext;
            if (declared is null || !declared.Valid(requirements) || request.Audience is not { IsValid: true } ||
                !request.RoleEntityIds.TryGetValue(declared.ObserverRole, out var observer) ||
                !request.RoleEntityIds.TryGetValue(declared.CampaignRole, out var campaign))
                return Denied();
            var audience = await audiences.ResolveAsync(campaign, cancellationToken);
            var grant = audience.Grant;
            if (!audience.Granted || grant is null || grant.CampaignId != campaign ||
                grant.Role == KnowledgeAudienceRole.Actor &&
                    (grant.ActorId != observer || request.Audience.Perspective != "player") ||
                grant.Role == KnowledgeAudienceRole.GameMaster && grant.ActorId is not null ||
                !Enum.IsDefined(grant.Role)) return Denied();
            var binding = await bindings.ResolveAsync(campaign, cancellationToken);
            if (binding is null || binding.StateSpaceId != request.StateSpaceId ||
                binding.ApplicationId != request.ApplicationId.Value || binding.CampaignEntityId != campaign)
                return Denied();
            binding.Validate();

            // All canonical reads below share this scoped DbContext and one SQLite read snapshot.
            // Existing graph-based knowledge owners are called only after a bounded count check.
            await using var transaction = db.Database.CurrentTransaction is null
                ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
            var edgeQuery = db.Set<ApplicationEcsRelationshipRecord>().AsNoTracking()
                .Where(row => row.StateSpaceId == request.StateSpaceId);
            var containmentQuery = db.Set<ApplicationEcsContainmentRecord>().AsNoTracking()
                .Where(row => row.StateSpaceId == request.StateSpaceId);
            if (await edgeQuery.Take(10001).CountAsync(cancellationToken) > 10000 ||
                await containmentQuery.Take(10001).CountAsync(cancellationToken) > 10000)
                return Unavailable();
            var member = await participation.ResolveAsync(binding, observer, cancellationToken);
            if (!member.Active || member.ActorMissing || string.IsNullOrWhiteSpace(member.Revision)) return Denied();
            var scope = await source.ReadCampaignScopeAsync(binding, cancellationToken);
            if (scope is null || scope.CampaignId != campaign) return Denied();

            using var input = JsonDocument.Parse(request.InputJson, new JsonDocumentOptions { MaxDepth = 8 });
            var selection = declared.SourceSets.Selection;
            if (!input.RootElement.TryGetProperty(selection.ItemInputField, out var selected) ||
                selected.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(selected.GetString()))
                return ProjectionResult.Failed("READ_MODEL_INPUT_INVALID");
            var selectedId = selected.GetString()!;
            var descendants = new List<ApplicationEcsContainmentRecord>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { observer };
            var frontier = new[] { observer };
            var complete = true;
            for (var depth = 0; depth < declared.MaxInventoryDepth && frontier.Length > 0; depth++)
            {
                var remaining = declared.MaxInventoryItems - descendants.Count;
                var layer = await containmentQuery.Where(row => frontier.Contains(row.ContainerEntityId))
                    .OrderBy(row => row.ContainerEntityId).ThenBy(row => row.Slot).ThenBy(row => row.ContainedEntityId)
                    .Take(remaining + 1).ToArrayAsync(cancellationToken);
                if (layer.Length > remaining) { complete = false; layer = layer.Take(remaining).ToArray(); }
                foreach (var row in layer)
                    if (!visited.Add(row.ContainedEntityId)) return Unavailable();
                descendants.AddRange(layer);
                frontier = layer.Select(row => row.ContainedEntityId).ToArray();
                if (!complete) break;
            }
            if (frontier.Length > 0 && await containmentQuery.AnyAsync(row =>
                    frontier.Contains(row.ContainerEntityId), cancellationToken)) complete = false;
            if (!descendants.Any(row => row.ContainedEntityId == selectedId))
                return ProjectionResult.Failed("READ_MODEL_SELECTION_UNAVAILABLE");

            var entityQuery = db.Set<ApplicationEcsEntityRecord>().AsNoTracking()
                .Where(row => row.StateSpaceId == request.StateSpaceId && row.DeletedAtUtc == null);
            var ids = descendants.Select(row => row.ContainedEntityId).Append(observer).Append(campaign).ToArray();
            var entities = await entityQuery.Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken);
            if (!entities.ContainsKey(selectedId)) return ProjectionResult.Failed("READ_MODEL_SELECTION_UNAVAILABLE");
            if (!entities.ContainsKey(observer) || !entities.ContainsKey(campaign)) return Denied();
            var componentQuery = db.Set<ApplicationEcsComponentRecord>().AsNoTracking()
                .Where(row => row.StateSpaceId == request.StateSpaceId);
            var observed = new List<object>();
            var materializedBytes = 0;
            var snapshot = new MechanicProjection { StateSpaceId = request.StateSpaceId, Input = request.InputJson,
                Seed = request.Seed, Audience = request.Audience };
            var role = requirements.Roles[declared.ObserverRole];

            async Task<Dictionary<string, string>> Components(string id, IEnumerable<string> localIds)
            {
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var local in localIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                {
                    if (!request.Mapping.Components.TryGetValue(local, out var mapping)) throw new InvalidOperationException();
                    var rows = await componentQuery.Where(row => row.EntityId == id &&
                        row.QualifiedTypeId == mapping.QualifiedTypeId).Take(2).ToArrayAsync(cancellationToken);
                    if (rows.Length > 1) throw new InvalidOperationException();
                    var row = rows.SingleOrDefault();
                    if (row is not null && (row.TypeVersion != mapping.TypeVersion || row.SchemaHash != mapping.SchemaHash))
                        throw new InvalidOperationException();
                    observed.Add(new { id, local, revision = row?.Revision, mapping.SchemaHash });
                    if (row is not null)
                    {
                        materializedBytes += Encoding.UTF8.GetByteCount(row.Data);
                        if (materializedBytes > 1_048_576) throw new InvalidOperationException();
                        result[local] = row.Data;
                    }
                }
                return result;
            }

            // Membership needs only the selected instance's references. Do not hydrate every item.
            var selectedComponents = await Components(selectedId, (role.ContentComponentIds ?? [])
                .Append(selection.DefinitionLinkComponentId));
            string? definitionId = null;
            if (selectedComponents.TryGetValue(selection.DefinitionLinkComponentId, out var link))
            {
                using var document = JsonDocument.Parse(link);
                if (document.RootElement.TryGetProperty(selection.DefinitionLinkField, out var reference) &&
                    reference.TryGetProperty("entityId", out var entityId) && entityId.ValueKind == JsonValueKind.String)
                    definitionId = entityId.GetString();
            }
            if (definitionId is not null)
            {
                var definition = await entityQuery.SingleOrDefaultAsync(row => row.Id == definitionId, cancellationToken);
                if (definition is null) return Unavailable();
                observed.Add(new { definition.Id, definition.Revision });
                var references = role.ComponentReferences ?? [];
                var idsToRead = references.Where(value => value.SourceComponentId == selection.DefinitionLinkComponentId &&
                    value.Field == selection.DefinitionLinkField).SelectMany(value =>
                    value.TargetComponentIds.Concat(value.OptionalTargetComponentIds ?? []));
                var values = await Components(definitionId, idsToRead);
                if (references.Where(value => value.SourceComponentId == selection.DefinitionLinkComponentId &&
                    value.Field == selection.DefinitionLinkField).SelectMany(value => value.TargetComponentIds)
                    .Any(id => !values.ContainsKey(id))) return Unavailable();
                snapshot.References[definitionId] = new(definitionId, values, definition.Name);
            }

            var sources = declared.SourceSets;
            // Discovery is a reference to this observer's existing knowledge edge. Never interpret
            // application booleans here, and never copy another observer's discovery record.
            if (sources.Discovery is { } discovery)
            {
                var data = await Components(selectedId, [discovery.ComponentId]);
                if (data.TryGetValue(discovery.ComponentId, out var json))
                {
                    using var document = JsonDocument.Parse(json);
                    if (document.RootElement.TryGetProperty(discovery.RelationshipField, out var edge) &&
                        edge.GetProperty("stateSpaceId").GetString() == request.StateSpaceId &&
                        edge.GetProperty("fromEntityId").GetString() == observer &&
                        edge.GetProperty("toEntityId").GetString() == selectedId &&
                        edge.GetProperty("qualifiedKind").GetString() == binding.ExplicitStateRelationshipKind &&
                        await edgeQuery.AnyAsync(row => row.FromEntityId == observer && row.ToEntityId == selectedId &&
                            row.QualifiedKind == binding.ExplicitStateRelationshipKind, cancellationToken))
                        selectedComponents[discovery.ComponentId] = json;
                }
            }
            // Optional raw facets are DM-only until an application projection declares a narrower
            // field-disclosure policy. Knowing one statement never reveals an entire component.
            if (request.Audience.Perspective == "dm")
                foreach (var pair in await Components(selectedId, sources.OptionalSelectedItemComponents
                    .Where(value => value != sources.Discovery?.ComponentId))) selectedComponents[pair.Key] = pair.Value;

            var candidateIds = new HashSet<string>(StringComparer.Ordinal) { selectedId };
            if (definitionId is not null) candidateIds.Add(definitionId);
            var linkedIds = new HashSet<string>(StringComparer.Ordinal);
            if (sources.Activities?.Linked is { } linkedSource)
            {
                foreach (var owner in new[] { selectedId, definitionId }.Where(id => id is not null).Distinct())
                {
                    var values = await Components(owner!, [linkedSource.ComponentId]);
                    if (!values.TryGetValue(linkedSource.ComponentId, out var json)) continue;
                    using var document = JsonDocument.Parse(json);
                    foreach (var id in ReferenceTargets(document.RootElement, linkedSource.Field + "[]"))
                    {
                        linkedIds.Add(id);
                        if (linkedIds.Count > declared.MaxKnowledgeCandidates) return Unavailable();
                    }
                    if (owner == selectedId) selectedComponents[linkedSource.ComponentId] = json;
                    else if (snapshot.References.TryGetValue(owner!, out var reference))
                        snapshot.References[owner!] = reference with { Components = reference.Components
                            .Append(new KeyValuePair<string, string>(linkedSource.ComponentId, json)).ToDictionary() };
                }
                candidateIds.UnionWith(linkedIds);
            }
            if (sources.Associations is { } associations)
            {
                if (!request.Mapping.Components.TryGetValue(associations.CandidateComponentId, out var mapping)) return Unavailable();
                var candidates = await componentQuery.Where(row => row.QualifiedTypeId == mapping.QualifiedTypeId)
                    .OrderBy(row => row.EntityId).Select(row => row.EntityId)
                    .Take(declared.MaxKnowledgeCandidates + 1).ToArrayAsync(cancellationToken);
                if (candidates.Length > declared.MaxKnowledgeCandidates) return Unavailable();
                candidateIds.UnionWith(candidates);
            }
            var targets = candidateIds.ToArray();
            var knowledgeIds = await edgeQuery.Where(row => row.QualifiedKind == binding.KnowledgeAboutRelationshipKind &&
                    targets.Contains(row.ToEntityId)).Select(row => row.FromEntityId).Distinct().OrderBy(value => value)
                .Take(declared.MaxKnowledgeCandidates + 1).ToArrayAsync(cancellationToken);
            if (knowledgeIds.Length > declared.MaxKnowledgeCandidates) return Unavailable();
            var stateIds = knowledgeIds.Append(selectedId).Concat(definitionId is null ? [] : new[] { definitionId }).Concat(linkedIds)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (stateIds.Length > declared.MaxKnowledgeCandidates) return Unavailable();
            var effective = await states.ResolveAllAsync(binding, observer, scope.WorldId, stateIds, cancellationToken);
            if (effective.Count != stateIds.Length || effective.Values.Any(value => value.WorldId != scope.WorldId)) return Unavailable();
            if (sources.Discovery is { } selectedDiscovery && !binding.ContentStates.Contains(
                    effective[selectedId].State, StringComparer.Ordinal))
                selectedComponents.Remove(selectedDiscovery.ComponentId);
            var contentIds = effective.Values.Where(value => binding.ContentStates.Contains(value.State, StringComparer.Ordinal))
                .Select(value => value.KnowledgeId).ToHashSet(StringComparer.Ordinal);
            var documents = new List<CanonicalKnowledgeDocument>();
            foreach (var id in knowledgeIds)
            {
                // Authorization precedes hydrating proposition text, subject labels or source IDs.
                if (request.Audience.Perspective != "dm" && !contentIds.Contains(id)) continue;
                var document = await source.ReadDocumentAsync(binding, scope.WorldId, id, cancellationToken);
                if (document is null) return Unavailable();
                if (document.Archived || document.ValidFromMinute > scope.CurrentMinute ||
                    document.ValidUntilMinute <= scope.CurrentMinute) continue;
                if (string.IsNullOrWhiteSpace(document.Summary) || document.Summary.Length > 1500) return Unavailable();
                documents.Add(document);
                materializedBytes += Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(document, Json));
                if (materializedBytes > 1_048_576) return Unavailable();
                snapshot.References[id] = new(id, new Dictionary<string, string>
                {
                    ["authorized-knowledge"] = JsonSerializer.Serialize(new { document.SubjectId, displayText = document.Summary,
                        document.PresentationKind, state = effective[id].State }, Json)
                });
            }
            if (sources.Associations is { } associationSource)
            {
                var labelIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in (request.Audience.Perspective == "dm" ? candidateIds :
                        documents.Select(value => value.SubjectId)).Distinct(StringComparer.Ordinal)
                    .Where(value => value != selectedId && value != definitionId).Order(StringComparer.Ordinal))
                {
                    var component = await Components(id, [associationSource.CandidateComponentId]);
                    if (component.Count == 0) continue;
                    var entity = await entityQuery.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
                    if (entity is null) return Unavailable();
                    observed.Add(new { entity.Id, entity.Revision });
                    snapshot.References[id] = new(id, component, entity.Name);
                    using var data = JsonDocument.Parse(component[associationSource.CandidateComponentId]);
                    foreach (var path in associationSource.ReferencePaths.Values)
                        foreach (var target in ReferenceTargets(data.RootElement, path))
                        {
                            labelIds.Add(target);
                            if (labelIds.Count > declared.MaxKnowledgeCandidates) return Unavailable();
                        }
                }
                // Declared references identify labels, not permission to read arbitrary entities.
                // Resolve their effective states before hydrating names; include negative lookups
                // and name/state revisions in the same authorized snapshot.
                var labelStates = await states.ResolveAllAsync(binding, observer, scope.WorldId, labelIds.ToArray(), cancellationToken);
                if (labelStates.Count != labelIds.Count || labelStates.Values.Any(value => value.WorldId != scope.WorldId)) return Unavailable();
                effective = effective.Concat(labelStates.Where(pair => !effective.ContainsKey(pair.Key))).ToDictionary(pair => pair.Key, pair => pair.Value);
                foreach (var id in labelIds.Order(StringComparer.Ordinal))
                {
                    if (request.Audience.Perspective != "dm" && labelStates[id].State != binding.BaselineState) continue;
                    var entity = await entityQuery.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
                    observed.Add(new { id, revision = entity?.Revision });
                    if (entity is not null && !snapshot.References.ContainsKey(id))
                        snapshot.References[id] = new(id, new Dictionary<string, string>(), entity.Name);
                }
            }
            // A statement about a linked record never grants its raw components. Only
            // effective baseline knowledge of the record itself (or DM) permits hydration.
            if (sources.Activities?.Linked is { } linked)
            {
                var labelIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var id in linkedIds.Order(StringComparer.Ordinal))
                {
                    if (request.Audience.Perspective != "dm" && effective[id].State != binding.BaselineState) continue;
                    var entity = await entityQuery.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
                    observed.Add(new { id, revision = entity?.Revision });
                    if (entity is null) continue;
                    var values = await Components(id, linked.TargetComponentIds);
                    snapshot.References[id] = new(id, values, entity.Name);
                    foreach (var path in linked.ReferencePaths)
                    {
                        if (!values.TryGetValue(path.Key, out var json)) continue;
                        using var document = JsonDocument.Parse(json);
                        foreach (var target in path.Value.SelectMany(value => ReferenceTargets(document.RootElement, value)))
                        {
                            labelIds.Add(target);
                            if (labelIds.Count > declared.MaxKnowledgeCandidates) return Unavailable();
                        }
                    }
                }
                var labelStates = await states.ResolveAllAsync(binding, observer, scope.WorldId, labelIds.ToArray(), cancellationToken);
                if (labelStates.Count != labelIds.Count || labelStates.Values.Any(value => value.WorldId != scope.WorldId)) return Unavailable();
                effective = effective.Concat(labelStates.Where(pair => !effective.ContainsKey(pair.Key))).ToDictionary(pair => pair.Key, pair => pair.Value);
                foreach (var id in labelIds.Order(StringComparer.Ordinal))
                {
                    if (request.Audience.Perspective != "dm" && labelStates[id].State != binding.BaselineState) continue;
                    var entity = await entityQuery.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
                    observed.Add(new { id, revision = entity?.Revision });
                    if (entity is not null && !snapshot.References.ContainsKey(id))
                        snapshot.References[id] = new(id, new Dictionary<string, string>(), entity.Name);
                }
            }
            // Inline records retain the existing DM-only raw-component boundary.
            if (sources.Activities is { } activity && request.Audience.Perspective == "dm")
            {
                foreach (var pair in await Components(selectedId, [activity.ComponentId])) selectedComponents[pair.Key] = pair.Value;
                if (definitionId is not null && snapshot.References.TryGetValue(definitionId, out var reference))
                    snapshot.References[definitionId] = reference with { Components = reference.Components
                        .Concat(await Components(definitionId, [activity.ComponentId])).ToDictionary(pair => pair.Key, pair => pair.Value) };
            }

            // Media remains application-shaped in the sandbox. Only audience-filtered,
            // finalized images receive opaque links; every content request replays this view.
            if (sources.Media is not null && media is not null && mediaLinks is not null && request.ReadModelQueryId is not null)
            {
                var mediaAudience = request.Audience.Perspective == "dm" ? EntityMediaAudience.GameMaster : EntityMediaAudience.Player;
                var viewRequest = new ApplicationReadModelRequest(request.StateSpaceId, request.ApplicationId,
                    request.ReadModelQueryId, request.RoleEntityIds, request.Audience, request.InputJson);
                foreach (var owner in new[] { selectedId, definitionId }.Where(id => id is not null).Distinct())
                {
                    EntityMediaDiscoveryResult discoveryResult;
                    try
                    {
                        discoveryResult = await media.DiscoverAsync(request.ApplicationId, request.StateSpaceId, owner!, mediaAudience,
                            diagnostics: false, cancellationToken);
                    }
                    catch (Exception exception) when (exception is EntityMediaException or IOException or DantesRoleplay.Blobs.BlobTransferException)
                    {
                        // An unavailable image is a neutral fallback, not a failed character view.
                        observed.Add(new { owner, media = "unavailable" });
                        continue;
                    }
                    var images = discoveryResult.Attachments.Where(value => value.MediaType is "image/png" or "image/jpeg" or "image/webp").Take(64).ToArray();
                    observed.Add(new { owner, media = Hash(images) });
                    var descriptors = images.Select(value => new
                    {
                        value.Role, value.Alt, value.Caption,
                        contentUrl = mediaLinks.GetOrCreate(new(viewRequest, campaign, observer, owner!, value.MediaId,
                            ReadModelMediaLinkStore.Fingerprint(value)))
                    }).ToArray();
                    var json = JsonSerializer.Serialize(new { attachments = descriptors }, Json);
                    if (owner == selectedId) selectedComponents["authorized-media"] = json;
                    else if (snapshot.References.TryGetValue(owner!, out var reference))
                        snapshot.References[owner!] = reference with { Components = reference.Components
                            .Append(new KeyValuePair<string, string>("authorized-media", json)).ToDictionary() };
                }
            }

            IReadOnlyList<ContainedProjection> Tree(string container) => descendants.Where(row => row.ContainerEntityId == container &&
                entities.ContainsKey(row.ContainedEntityId)).Select(row => new ContainedProjection(row.ContainedEntityId,
                    entities[row.ContainedEntityId].Name, row.Slot,
                    row.ContainedEntityId == selectedId ? selectedComponents : null, Tree(row.ContainedEntityId))).ToArray();
            foreach (var pair in request.RoleEntityIds)
            {
                var declaration = requirements.Roles[pair.Key];
                var components = await Components(pair.Value, declaration.Components.Concat(declaration.OptionalComponents ?? []));
                if (declaration.Components.Any(id => !components.ContainsKey(id))) return Unavailable();
                snapshot.Roles[pair.Key] = new(pair.Value, entities[pair.Value].Name, components,
                    Contains: pair.Key == declared.ObserverRole ? Tree(observer) : null);
            }
            var graph = await edgeQuery.OrderBy(row => row.FromEntityId).ThenBy(row => row.ToEntityId).ThenBy(row => row.QualifiedKind)
                .Select(row => new { row.FromEntityId, row.ToEntityId, row.QualifiedKind, row.Revision }).ToArrayAsync(cancellationToken);
            var knowledgeRevision = Hash(new { scope.Revision, graph, knowledgeIds,
                effective = effective.OrderBy(pair => pair.Key).Select(pair => new { pair.Key, pair.Value.Revision }),
                documents = documents.Select(value => new { value.KnowledgeId, value.Revision }) });
            var inventoryRevision = Hash(new { complete, observed, descendants = descendants.Select(row =>
                new { row.ContainerEntityId, row.ContainedEntityId, row.Slot, row.Revision }),
                entities = entities.Values.OrderBy(row => row.Id).Select(row => new { row.Id, row.Revision }) });
            var fingerprint = Convert.ToHexString(HMACSHA256.HashData(RevisionKey, Encoding.UTF8.GetBytes(Hash(new { request.StateSpaceId, request.ApplicationId.Value, observer, campaign,
                request.Audience.Perspective, grant.PrincipalId, grant.PolicyRevision, binding.BindingRevision,
                member.Revision, inventoryRevision, knowledgeRevision }))));
            var currentGrant = await audiences.ResolveAsync(campaign, cancellationToken);
            if (!currentGrant.Granted || currentGrant.Grant != grant) return ProjectionResult.Failed("READ_MODEL_SOURCE_STALE");
            if (input.RootElement.TryGetProperty("expectedSourceRevision", out var expectedSource) &&
                expectedSource.ValueKind != JsonValueKind.Null && expectedSource.GetString() != fingerprint)
                return ProjectionResult.Failed("READ_MODEL_SOURCE_STALE");
            snapshot = snapshot with { AuthorizedSourceRevision = fingerprint, AuthorizedObserver = JsonSerializer.SerializeToElement(new
            {
                version = 1, applicationId = request.ApplicationId.Value, stateSpaceId = request.StateSpaceId,
                campaignId = campaign, observerId = observer, perspective = request.Audience.Perspective,
                policyRevision = Hash(grant.PolicyRevision), participationRevision = Hash(member.Revision),
                bindingRevision = Hash(binding.BindingRevision), inventoryRevision, knowledgeRevision,
                authorizedSourceRevision = fingerprint, inventoryComplete = complete, knowledgeComplete = true,
                knowledge = effective.OrderBy(pair => pair.Key).Select(pair => new { knowledgeId = pair.Key,
                    pair.Value.State, pair.Value.SourceKind, pair.Value.SourceEntityId, pair.Value.Revision })
            }, Json) };
            if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(snapshot, Json)) > 1_048_576) return Unavailable();
            return new(snapshot, []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Unavailable(); }
    }

    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));
    private static IEnumerable<string> ReferenceTargets(JsonElement root, string path)
    {
        IEnumerable<JsonElement> nodes = [root];
        foreach (var part in path.Split('.'))
        {
            var array = part.EndsWith("[]", StringComparison.Ordinal);
            var key = array ? part[..^2] : part;
            nodes = nodes.SelectMany(node => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value)
                ? array && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : array ? [] : new[] { value }
                : []);
        }
        return nodes.Where(node => node.ValueKind == JsonValueKind.Object && node.TryGetProperty("entityId", out var id) && id.ValueKind == JsonValueKind.String)
            .Select(node => node.GetProperty("entityId").GetString()!).Where(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 200);
    }
    private static ProjectionResult Denied() => ProjectionResult.Failed("READ_MODEL_FORBIDDEN");
    private static ProjectionResult Unavailable() => ProjectionResult.Failed("READ_MODEL_UNAVAILABLE");
}
