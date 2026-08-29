using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Ecs;

namespace DantesRoleplay.Knowledge;

/// <summary>Read-only canonical projection over one exact application state space.</summary>
public sealed class ApplicationKnowledgeCanonicalSource(
    IStateSpaceRegistry stateSpaces,
    IEntityComponentStore entities,
    IStateSpaceEdgeStore edges) : IKnowledgeCanonicalSource
{
    private const int PageSize = 100;
    private const int MaximumEntities = 10_000;
    private const int MaximumComponentsPerEntity = 2_000;

    public async Task<KnowledgeCampaignScope?> ReadCampaignScopeAsync(
        KnowledgeApplicationBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        binding.Validate();
        var stateSpace = stateSpaces.Get(binding.StateSpaceId);
        if (stateSpace is null ||
            stateSpace.ApplicationRevision.ApplicationId.Value != binding.ApplicationId)
            return null;

        var campaign = await entities.GetEntityAsync(
            binding.StateSpaceId, binding.CampaignEntityId, cancellationToken);
        if (campaign is null || !await HasStatusAsync(binding.StateSpaceId, campaign.EntityId,
                binding.CampaignRootComponentTypeId, binding.CampaignStatusProperty,
                binding.ActiveCampaignStatus, cancellationToken))
            return null;

        var relationships = await edges.ListRelationshipsAsync(binding.StateSpaceId, cancellationToken);
        var worlds = relationships.Where(value =>
                value.FromEntityId == binding.CampaignEntityId &&
                value.QualifiedKind == binding.CampaignWorldRelationshipKind && Empty(value.DataJson))
            .Select(value => value.ToEntityId).Distinct(StringComparer.Ordinal).ToArray();
        if (worlds.Length != 1) return null;

        var world = await entities.GetEntityAsync(binding.StateSpaceId, worlds[0], cancellationToken);
        if (world is null || !await HasStatusAsync(binding.StateSpaceId, world.EntityId,
                binding.WorldRootComponentTypeId, binding.WorldStatusProperty,
                binding.ActiveWorldStatus, cancellationToken))
            return null;
        var clock = await entities.GetComponentAsync(
            binding.StateSpaceId, world.EntityId, binding.WorldClockComponentTypeId, cancellationToken);
        if (clock is null || !Integer(clock.ValueJson, binding.CurrentMinuteProperty, out var minute))
            return null;

        var campaignWorldEdge = relationships.Single(value =>
            value.FromEntityId == binding.CampaignEntityId && value.ToEntityId == world.EntityId &&
            value.QualifiedKind == binding.CampaignWorldRelationshipKind && Empty(value.DataJson));
        var revision = Hash(new
        {
            StateSpaceRevision = stateSpace.BindingRevision,
            CampaignRevision = campaign.Revision,
            WorldRevision = world.Revision,
            ClockRevision = clock.Revision,
            minute,
            CampaignWorldRevision = campaignWorldEdge.Revision
        });
        return new(binding.CampaignEntityId, world.EntityId, minute, revision);
    }

    public async Task<KnowledgeCampaignProjection?> ReadWorldAsync(
        KnowledgeApplicationBinding binding,
        KnowledgeCampaignScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(scope);
        binding.Validate();
        if (scope.CampaignId != binding.CampaignEntityId || !Bounded(scope.WorldId) ||
            scope.CurrentMinute is < 0 or > 1_000_000_000 || !Bounded(scope.Revision)) return null;
        var stateSpace = stateSpaces.Get(binding.StateSpaceId);
        if (stateSpace is null || stateSpace.ApplicationRevision.ApplicationId.Value != binding.ApplicationId)
            return null;
        var relationships = await edges.ListRelationshipsAsync(binding.StateSpaceId, cancellationToken);
        var projected = new List<CanonicalKnowledgeDocument>();
        string? cursor = null;
        var seen = 0;
        do
        {
            var page = await entities.ListEntitiesAsync(
                binding.StateSpaceId, cursor, PageSize, cancellationToken);
            seen += page.Entities.Count;
            if (seen > MaximumEntities) return null;
            foreach (var entity in page.Entities)
            {
                var document = await ProjectAsync(
                    binding, scope.WorldId, entity, relationships, cancellationToken);
                if (document is not null) projected.Add(document);
            }
            cursor = page.NextEntityId;
        } while (cursor is not null);

        projected.Sort((left, right) => StringComparer.Ordinal.Compare(left.KnowledgeId, right.KnowledgeId));
        var revision = Hash(new
        {
            scope.Revision,
            worldEdges = relationships.Where(value => value.ToEntityId == scope.WorldId)
                .Select(value => new { value.FromEntityId, value.ToEntityId, value.QualifiedKind, value.Revision }),
            documents = projected.Select(value => new { value.KnowledgeId, value.Revision })
        });
        return new(scope, revision, projected);
    }

    public async Task<CanonicalKnowledgeDocument?> ReadDocumentAsync(
        KnowledgeApplicationBinding binding,
        string worldId,
        string knowledgeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        binding.Validate();
        if (!Bounded(worldId) || !Bounded(knowledgeId)) return null;
        var stateSpace = stateSpaces.Get(binding.StateSpaceId);
        if (stateSpace is null || stateSpace.ApplicationRevision.ApplicationId.Value != binding.ApplicationId)
            return null;
        var entity = await entities.GetEntityAsync(binding.StateSpaceId, knowledgeId, cancellationToken);
        if (entity is null) return null;
        var relationships = await edges.ListRelationshipsAsync(binding.StateSpaceId, cancellationToken);
        return await ProjectAsync(binding, worldId, entity, relationships, cancellationToken);
    }

    private async Task<CanonicalKnowledgeDocument?> ProjectAsync(
        KnowledgeApplicationBinding binding,
        string worldId,
        EcsEntityView entity,
        IReadOnlyList<EcsRelationshipView> relationships,
        CancellationToken cancellationToken)
    {
        var components = await ReadComponentsAsync(binding.StateSpaceId, entity.EntityId, cancellationToken);
        if (components is null) return null;
        var primary = components.Where(component => binding.KnowledgeKinds.Any(kind =>
            kind.ComponentTypeId == component.Type.QualifiedTypeId)).ToArray();
        if (primary.Length != 1) return null;
        var kind = binding.KnowledgeKinds.Single(value =>
            value.ComponentTypeId == primary[0].Type.QualifiedTypeId);

        var classification = components.Where(value =>
            value.Type.QualifiedTypeId == binding.ClassificationComponentTypeId).ToArray();
        if (classification.Length != 1 ||
            !Text(classification[0].ValueJson, binding.ClassificationSensitivityProperty, out _))
            return null;
        if (!Text(primary[0].ValueJson, binding.PrimaryStatusProperty, out var status) ||
            !Text(primary[0].ValueJson, binding.PrimarySummaryProperty, out var summary))
            return null;

        var scopedWorlds = relationships.Where(value =>
                value.FromEntityId == entity.EntityId &&
                value.QualifiedKind == binding.KnowledgeWorldRelationshipKind && Empty(value.DataJson))
            .Select(value => value.ToEntityId).ToArray();
        var subjects = relationships.Where(value =>
                value.FromEntityId == entity.EntityId &&
                value.QualifiedKind == binding.KnowledgeAboutRelationshipKind && Empty(value.DataJson))
            .Select(value => value.ToEntityId).ToArray();
        if (scopedWorlds.Length != 1 || scopedWorlds[0] != worldId || subjects.Length != 1)
            return null;
        var subject = await entities.GetEntityAsync(
            binding.StateSpaceId, subjects[0], cancellationToken);
        if (subject is null) return null;

        var validity = components.Where(value =>
            value.Type.QualifiedTypeId == binding.ValidityComponentTypeId).ToArray();
        if (validity.Length > 1 || !Interval(validity.SingleOrDefault()?.ValueJson, binding,
                out var validFrom, out var validUntil))
            return null;

        var display = BoundedText(string.Join('\n', new[] { entity.Name.Trim(), summary, subject.Name.Trim() }
            .Where(value => value.Length > 0)), 1_500);
        if (display.Length == 0) return null;
        var search = BoundedText(display, 4_000);
        var relevantEdges = relationships.Where(value => value.FromEntityId == entity.EntityId)
            .Select(value => new
            {
                value.FromEntityId, value.ToEntityId, value.QualifiedKind, value.DataJson, value.Revision
            });
        var revision = Hash(new
        {
            entity.EntityId,
            entity.Revision,
            primary = new { primary[0].Type.QualifiedTypeId, primary[0].Type.TypeVersion,
                primary[0].Type.SchemaHash, primary[0].Revision, primary[0].ValueJson },
            classification = new { classification[0].Type.TypeVersion,
                classification[0].Type.SchemaHash, classification[0].Revision,
                classification[0].ValueJson },
            validity = validity.Select(value => new { value.Type.TypeVersion, value.Type.SchemaHash,
                value.Revision, value.ValueJson }),
            subject = new { subject.EntityId, subject.Name, subject.Revision },
            relevantEdges
        });
        return new(entity.EntityId, worldId, kind.Kind, status,
            kind.ArchivedStatuses.Contains(status, StringComparer.Ordinal), subject.EntityId,
            validFrom, validUntil, display, search, kind.PresentationKind, revision);
    }

    private async Task<IReadOnlyList<EcsComponentView>?> ReadComponentsAsync(
        string stateSpaceId,
        string entityId,
        CancellationToken cancellationToken)
    {
        var result = new List<EcsComponentView>();
        string? cursor = null;
        do
        {
            var page = await entities.ListComponentsAsync(
                stateSpaceId, entityId, cursor, PageSize, cancellationToken);
            result.AddRange(page.Components);
            if (result.Count > MaximumComponentsPerEntity) return null;
            cursor = page.NextQualifiedTypeId;
        } while (cursor is not null);
        return result;
    }

    private async Task<bool> HasStatusAsync(
        string stateSpaceId,
        string entityId,
        string componentTypeId,
        string property,
        string expected,
        CancellationToken cancellationToken)
    {
        var component = await entities.GetComponentAsync(
            stateSpaceId, entityId, componentTypeId, cancellationToken);
        return component is not null && Text(component.ValueJson, property, out var value) && value == expected;
    }

    private static bool Interval(
        string? json,
        KnowledgeApplicationBinding binding,
        out long? from,
        out long? until)
    {
        from = until = null;
        if (json is null) return true;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(binding.ValidFromProperty, out var start) ||
                !start.TryGetInt64(out var startValue) || startValue is < 0 or > 1_000_000_000)
                return false;
            from = startValue;
            if (!root.TryGetProperty(binding.ValidUntilProperty, out var end)) return true;
            if (!end.TryGetInt64(out var endValue) || endValue <= startValue || endValue > 1_000_000_000)
                return false;
            until = endValue;
            return true;
        }
        catch (JsonException) { return false; }
    }

    internal static bool Empty(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   !document.RootElement.EnumerateObject().Any();
        }
        catch (JsonException) { return false; }
    }

    internal static bool Text(string json, string property, out string value)
    {
        value = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(property, out var element) ||
                element.ValueKind != JsonValueKind.String) return false;
            value = element.GetString()?.Trim() ?? string.Empty;
            return value.Length is > 0 and <= 1_500;
        }
        catch (JsonException) { return false; }
    }

    private static bool Integer(string json, string property, out long value)
    {
        value = 0;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty(property, out var element) &&
                   element.TryGetInt64(out value) && value is >= 0 and <= 1_000_000_000;
        }
        catch (JsonException) { return false; }
    }

    internal static string Hash(object value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static bool Bounded(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= 200;

    private static string BoundedText(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];
}
