using System.Text.Json;
using DantesRoleplay.Ecs;
using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.Projections;
using Microsoft.AspNetCore.Http;

namespace DantesRoleplay.MCPServer;

/// <summary>
/// Read-only, application-bound projection of dated World records. All ruleset vocabulary comes
/// from the activated application binding and all audience identity comes from the ambient host.
/// </summary>
internal static class WorldChronologyWebEndpoint
{
    private const int PageSize = 100;
    private const int MaximumEntities = 10_000;
    private const int MaximumEdges = 10_000;
    private const int MaximumEntries = 500;
    private const int MaximumSubjects = 10;

    public static async Task<IResult> ReadAsync(
        string applicationId,
        string campaignId,
        string? perspective,
        HttpContext context,
        ILocalKnowledgeSeatProvider seats,
        IAuthorizedKnowledgeAudiencePolicy audiences,
        IKnowledgeApplicationBindingResolver bindings,
        IWorldChronologyBindingResolver chronologyBindings,
        IKnowledgeActorParticipationVerifier participation,
        IEntityComponentStore entities,
        IStateSpaceEdgeStore edges,
        IProjectionReadTransaction transactions,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!Token(applicationId) || !Token(campaignId) || perspective is not ("player" or "dm"))
            return Error("INVALID_CHRONOLOGY_REQUEST", StatusCodes.Status400BadRequest);

        var audience = await SystemAudienceContextHandler.ResolveAsync(
            seats, audiences, bindings, participation, campaignId, cancellationToken);
        if (audience.Error is not null || !BoundAudience(audience.Data))
            return Error("CHRONOLOGY_UNAVAILABLE", StatusCodes.Status403Forbidden);

        var seat = seats.Current();
        if (seat.ApplicationId != applicationId ||
            (seat.Role != KnowledgeAudienceRole.GameMaster && seat.CampaignId != campaignId) ||
            (perspective == "dm" && seat.Role != KnowledgeAudienceRole.GameMaster))
            return Error("CHRONOLOGY_UNAVAILABLE", StatusCodes.Status403Forbidden);

        var binding = await bindings.ResolveAsync(campaignId, cancellationToken);
        if (binding is null || binding.ApplicationId != applicationId ||
            binding.CampaignEntityId != campaignId)
            return Results.NotFound();

        try
        {
            binding.Validate();
            var chronology = await chronologyBindings.ResolveAsync(binding, cancellationToken);
            if (chronology is null)
                return Error("CHRONOLOGY_UNAVAILABLE", StatusCodes.Status503ServiceUnavailable);
            var projection = await transactions.ExecuteAsync(token => ProjectAsync(
                binding, chronology, perspective, entities, edges, token), cancellationToken);
            if (projection is null)
                return Error("CHRONOLOGY_UNAVAILABLE", StatusCodes.Status503ServiceUnavailable);

            var responseEntries = projection.Select((entry, index) => perspective == "player"
                ? (object)new
                {
                    id = $"chronology-{index + 1}",
                    occurredAtMinute = entry.OccurredAtMinute,
                    dateLabel = entry.DateLabel,
                    precision = entry.Precision,
                    title = entry.Title,
                    summary = entry.Summary
                }
                : new
                {
                    id = $"chronology-{index + 1}",
                    occurredAtMinute = entry.OccurredAtMinute,
                    dateLabel = entry.DateLabel,
                    precision = entry.Precision,
                    title = entry.Title,
                    summary = entry.Summary,
                    subjects = entry.Subjects.Select(subject => new
                    {
                        id = subject.Id,
                        name = subject.Name
                    }).ToArray()
                }).ToArray();
            return Results.Json(new
            {
                status = responseEntries.Length == 0 ? "empty" : "ready",
                perspective,
                entries = responseEntries
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Error("CHRONOLOGY_UNAVAILABLE", StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IReadOnlyList<ProjectedEntry>?> ProjectAsync(
        KnowledgeApplicationBinding binding,
        WorldChronologyBinding chronologyBinding,
        string perspective,
        IEntityComponentStore entities,
        IStateSpaceEdgeStore edges,
        CancellationToken cancellationToken)
    {
        var relationships = await edges.ListRelationshipsAsync(binding.StateSpaceId, cancellationToken);
        var containments = await edges.ListContainmentsAsync(binding.StateSpaceId, cancellationToken);
        if (relationships.Count > MaximumEdges || containments.Count > MaximumEdges) return null;

        var campaignWorldEdges = relationships.Where(value =>
            value.FromEntityId == binding.CampaignEntityId &&
            value.QualifiedKind == binding.CampaignWorldRelationshipKind).ToArray();
        if (campaignWorldEdges.Length != 1 || !Empty(campaignWorldEdges[0].DataJson)) return null;
        var worldId = campaignWorldEdges[0].ToEntityId;
        var world = await entities.GetEntityAsync(binding.StateSpaceId, worldId, cancellationToken);
        if (world is null || world.DeletedAtUtc is not null ||
            !await ExactStatusAsync(binding.StateSpaceId, worldId, binding.WorldRootComponentTypeId,
                binding.WorldStatusProperty, binding.ActiveWorldStatus, entities, cancellationToken))
            return null;

        var clock = await entities.GetComponentAsync(
            binding.StateSpaceId, worldId, binding.WorldClockComponentTypeId, cancellationToken);
        if (clock is null || !TextProperty(clock.ValueJson,
                chronologyBinding.WorldClockCalendarIdProperty, 100, out var calendarId))
            return null;

        var parentByEntity = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var containment in containments)
            if (!parentByEntity.TryAdd(containment.ContainedEntityId, containment.ContainerEntityId))
                return null;

        if (entities is IEntityComponentProjectionStore selections)
            return await ProjectSelectedAsync(binding, chronologyBinding, perspective, worldId,
                calendarId, relationships, parentByEntity, selections, cancellationToken);

        var result = new List<ProjectedEntry>();
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
                if (entity.DeletedAtUtc is not null) continue;
                var component = await entities.GetComponentAsync(
                    binding.StateSpaceId, entity.EntityId,
                    chronologyBinding.ComponentTypeId, cancellationToken);
                if (component is null) continue;

                var scopes = relationships.Where(value =>
                    value.FromEntityId == entity.EntityId &&
                    value.QualifiedKind == chronologyBinding.InWorldRelationshipKind).ToArray();
                if (!scopes.Any(value => value.ToEntityId == worldId)) continue;
                if (scopes.Length != 1 || !Empty(scopes[0].DataJson)) return null;

                if (!TryRead(component.ValueJson, chronologyBinding, out var chronology)) return null;
                if (chronology.Status == chronologyBinding.ArchivedStatus) continue;
                if (chronology.Status != chronologyBinding.ActiveStatus) return null;
                var allowed = chronology.Visibility == chronologyBinding.PublicVisibility ||
                    chronology.Visibility == chronologyBinding.PartyVisibility ||
                    (perspective == "dm" &&
                        chronology.Visibility == chronologyBinding.GameMasterVisibility);
                if (!allowed) continue;
                if (chronology.CalendarId != calendarId) return null;
                var about = relationships.Where(value =>
                    value.FromEntityId == entity.EntityId &&
                    value.QualifiedKind == chronologyBinding.AboutRelationshipKind).ToArray();
                if (about.Length > MaximumSubjects || about.Any(value => !Empty(value.DataJson)) ||
                    about.Select(value => value.ToEntityId).Distinct(StringComparer.Ordinal).Count() != about.Length)
                    return null;

                var subjects = new List<ProjectedSubject>();
                foreach (var subjectEdge in about.OrderBy(value => value.ToEntityId, StringComparer.Ordinal))
                {
                    if (!InWorld(subjectEdge.ToEntityId, worldId, parentByEntity,
                            relationships, chronologyBinding.SubjectWorldRelationshipKinds))
                        return null;
                    var subject = await entities.GetEntityAsync(
                        binding.StateSpaceId, subjectEdge.ToEntityId, cancellationToken);
                    if (subject is null || subject.DeletedAtUtc is not null ||
                        !DisplayText(subject.Name, 200, out var subjectName)) return null;
                    subjects.Add(new(subject.EntityId, subjectName));
                }

                result.Add(new(entity.EntityId, chronology.OccurredAtMinute, chronology.DateLabel,
                    chronology.Precision, chronology.Title, chronology.Summary, subjects));
                if (result.Count > MaximumEntries) return null;
            }
            cursor = page.NextEntityId;
        } while (cursor is not null);

        result.Sort((left, right) =>
        {
            var minute = left.OccurredAtMinute.CompareTo(right.OccurredAtMinute);
            return minute != 0 ? minute : StringComparer.Ordinal.Compare(left.CanonicalId, right.CanonicalId);
        });
        return result;
    }

    private static async Task<IReadOnlyList<ProjectedEntry>?> ProjectSelectedAsync(
        KnowledgeApplicationBinding binding,
        WorldChronologyBinding chronologyBinding,
        string perspective,
        string worldId,
        string calendarId,
        IReadOnlyList<EcsRelationshipView> relationships,
        IReadOnlyDictionary<string, string> parentByEntity,
        IEntityComponentProjectionStore selections,
        CancellationToken cancellationToken)
    {
        var result = new List<ProjectedEntry>();
        string? cursor = null;
        var seen = 0;
        do
        {
            var page = await selections.SelectAsync(binding.StateSpaceId,
                new([chronologyBinding.ComponentTypeId], [chronologyBinding.ComponentTypeId], cursor, 1_000),
                cancellationToken);
            seen += page.Entities.Count;
            if (seen > MaximumEntities) return null;
            var pageIds = page.Entities.Select(value => value.Entity.EntityId).ToHashSet(StringComparer.Ordinal);
            var subjectIds = relationships.Where(value => pageIds.Contains(value.FromEntityId) &&
                    value.QualifiedKind == chronologyBinding.AboutRelationshipKind)
                .Select(value => value.ToEntityId).Distinct(StringComparer.Ordinal).ToArray();
            var subjects = new Dictionary<string, EcsEntityView>(StringComparer.Ordinal);
            foreach (var chunk in subjectIds.Chunk(1_000))
            {
                var rows = await selections.GetAsync(binding.StateSpaceId, chunk,
                    [chronologyBinding.ComponentTypeId], cancellationToken);
                foreach (var row in rows) subjects[row.Entity.EntityId] = row.Entity;
            }

            foreach (var candidate in page.Entities)
            {
                var component = candidate.Components.SingleOrDefault(value =>
                    value.Type.QualifiedTypeId == chronologyBinding.ComponentTypeId);
                if (component is null) return null;
                var projected = ProjectCandidate(candidate.Entity, component, chronologyBinding,
                    perspective, worldId, calendarId, relationships, parentByEntity, subjects);
                if (!projected.Valid) return null;
                if (projected.Entry is not null)
                {
                    result.Add(projected.Entry);
                    if (result.Count > MaximumEntries) return null;
                }
            }
            cursor = page.NextEntityId;
        } while (cursor is not null);

        result.Sort((left, right) =>
        {
            var minute = left.OccurredAtMinute.CompareTo(right.OccurredAtMinute);
            return minute != 0 ? minute : StringComparer.Ordinal.Compare(left.CanonicalId, right.CanonicalId);
        });
        return result;
    }

    private static (bool Valid, ProjectedEntry? Entry) ProjectCandidate(
        EcsEntityView entity,
        EcsComponentView component,
        WorldChronologyBinding chronologyBinding,
        string perspective,
        string worldId,
        string calendarId,
        IReadOnlyList<EcsRelationshipView> relationships,
        IReadOnlyDictionary<string, string> parentByEntity,
        IReadOnlyDictionary<string, EcsEntityView> subjectsById)
    {
        var scopes = relationships.Where(value => value.FromEntityId == entity.EntityId &&
            value.QualifiedKind == chronologyBinding.InWorldRelationshipKind).ToArray();
        if (!scopes.Any(value => value.ToEntityId == worldId)) return (true, null);
        if (scopes.Length != 1 || !Empty(scopes[0].DataJson)) return (false, null);
        if (!TryRead(component.ValueJson, chronologyBinding, out var chronology)) return (false, null);
        if (chronology.Status == chronologyBinding.ArchivedStatus) return (true, null);
        if (chronology.Status != chronologyBinding.ActiveStatus) return (false, null);
        var allowed = chronology.Visibility == chronologyBinding.PublicVisibility ||
            chronology.Visibility == chronologyBinding.PartyVisibility ||
            (perspective == "dm" && chronology.Visibility == chronologyBinding.GameMasterVisibility);
        if (!allowed) return (true, null);
        if (chronology.CalendarId != calendarId) return (false, null);
        var about = relationships.Where(value => value.FromEntityId == entity.EntityId &&
            value.QualifiedKind == chronologyBinding.AboutRelationshipKind).ToArray();
        if (about.Length > MaximumSubjects || about.Any(value => !Empty(value.DataJson)) ||
            about.Select(value => value.ToEntityId).Distinct(StringComparer.Ordinal).Count() != about.Length)
            return (false, null);
        var subjects = new List<ProjectedSubject>();
        foreach (var subjectEdge in about.OrderBy(value => value.ToEntityId, StringComparer.Ordinal))
        {
            if (!InWorld(subjectEdge.ToEntityId, worldId, parentByEntity,
                    relationships, chronologyBinding.SubjectWorldRelationshipKinds) ||
                !subjectsById.TryGetValue(subjectEdge.ToEntityId, out var subject) ||
                !DisplayText(subject.Name, 200, out var subjectName)) return (false, null);
            subjects.Add(new(subject.EntityId, subjectName));
        }
        return (true, new(entity.EntityId, chronology.OccurredAtMinute, chronology.DateLabel,
            chronology.Precision, chronology.Title, chronology.Summary, subjects));
    }

    private static bool TryRead(
        string json,
        WorldChronologyBinding binding,
        out ChronologyValue value)
    {
        value = null!;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 8 ||
                !TextProperty(root, binding.StatusProperty, 100, out var status) ||
                !TextProperty(root, binding.TitleProperty, 160, out var title, display: true) ||
                !TextProperty(root, binding.SummaryProperty, 1_000, out var summary, display: true) ||
                !TextProperty(root, binding.CalendarIdProperty, 100, out var calendarId) ||
                !root.TryGetProperty(binding.OccurredAtMinuteProperty, out var minuteElement) ||
                !minuteElement.TryGetInt64(out var minute) || minute is < -1_000_000_000 or > 1_000_000_000 ||
                !TextProperty(root, binding.PrecisionProperty, 100, out var precision) ||
                !binding.Precisions.Contains(precision, StringComparer.Ordinal) ||
                !TextProperty(root, binding.DateLabelProperty, 100, out var dateLabel, display: true) ||
                !TextProperty(root, binding.VisibilityProperty, 100, out var visibility) ||
                !(visibility == binding.PublicVisibility || visibility == binding.PartyVisibility ||
                    visibility == binding.GameMasterVisibility) ||
                !(status == binding.ActiveStatus || status == binding.ArchivedStatus))
                return false;
            value = new(status, title, summary, calendarId, minute, precision, dateLabel, visibility);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool InWorld(
        string entityId,
        string worldId,
        IReadOnlyDictionary<string, string> parentByEntity,
        IReadOnlyList<EcsRelationshipView> relationships,
        IReadOnlyList<string> subjectWorldRelationshipKinds)
    {
        if (entityId == worldId) return true;
        var directScopes = relationships.Where(value =>
            value.FromEntityId == entityId &&
            subjectWorldRelationshipKinds.Contains(value.QualifiedKind, StringComparer.Ordinal)).ToArray();
        if (directScopes.Length > 0)
            return directScopes.Length == 1 && directScopes[0].ToEntityId == worldId &&
                Empty(directScopes[0].DataJson);

        var current = entityId;
        var visited = new HashSet<string>(StringComparer.Ordinal) { current };
        while (parentByEntity.TryGetValue(current, out var parent))
        {
            if (parent == worldId) return true;
            if (!visited.Add(parent)) return false;
            current = parent;
        }
        return false;
    }

    private static async Task<bool> ExactStatusAsync(
        string stateSpaceId,
        string entityId,
        string componentTypeId,
        string property,
        string expected,
        IEntityComponentStore entities,
        CancellationToken cancellationToken)
    {
        var component = await entities.GetComponentAsync(
            stateSpaceId, entityId, componentTypeId, cancellationToken);
        return component is not null && TextProperty(component.ValueJson, property, 100, out var value) &&
            value == expected;
    }

    private static bool BoundAudience(object? data)
    {
        try
        {
            var value = JsonSerializer.SerializeToElement(data);
            return value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty("status", out var status) && status.GetString() == "bound";
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TextProperty(string json, string property, int maximum, out string value)
    {
        value = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            return TextProperty(document.RootElement, property, maximum, out value);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TextProperty(
        JsonElement root,
        string property,
        int maximum,
        out string value,
        bool display = false)
    {
        value = string.Empty;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        var text = element.GetString() ?? string.Empty;
        if (text.Length is < 1 || text.Length > maximum || text != text.Trim() ||
            (!display && text.Any(char.IsWhiteSpace))) return false;
        value = text;
        return true;
    }

    private static bool DisplayText(string text, int maximum, out string value)
    {
        value = text;
        return text.Length is > 0 && text.Length <= maximum && text == text.Trim();
    }

    private static bool Empty(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                !document.RootElement.EnumerateObject().Any();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool Token(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value == value.Trim() && value.Length <= 200 && !value.Any(char.IsWhiteSpace);

    private static IResult Error(string code, int statusCode) =>
        Results.Json(new { error = code }, statusCode: statusCode);

    private sealed record ChronologyValue(
        string Status,
        string Title,
        string Summary,
        string CalendarId,
        long OccurredAtMinute,
        string Precision,
        string DateLabel,
        string Visibility);

    private sealed record ProjectedSubject(string Id, string Name);

    private sealed record ProjectedEntry(
        string CanonicalId,
        long OccurredAtMinute,
        string DateLabel,
        string Precision,
        string Title,
        string Summary,
        IReadOnlyList<ProjectedSubject> Subjects);
}
