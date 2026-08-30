namespace DantesRoleplay.Knowledge;

/// <summary>
/// Produces an ID-free notebook only after ambient authorization, exact application binding,
/// campaign participation, canonical world projection, and effective actor-state filtering.
/// </summary>
public sealed class AuthorizedKnowledgeNotebookReader(
    IAuthorizedKnowledgeAudiencePolicy audience,
    IKnowledgeApplicationBindingResolver bindings,
    IKnowledgeActorParticipationVerifier participation,
    IKnowledgeCanonicalSource source,
    IKnowledgeEffectiveStateResolver states,
    IKnowledgeLexicalRetriever lexical) : IAuthorizedKnowledgeNotebookReader
{
    public async Task<AuthorizedKnowledgeNotebookResult> ReadAsync(
        AuthorizedKnowledgeNotebookRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Valid(request)) return new("invalid", [], [], "INVALID_KNOWLEDGE_REQUEST");

        KnowledgeAudienceResolution resolvedAudience;
        try { resolvedAudience = await audience.ResolveAsync(request.CampaignId, cancellationToken); }
        catch { return AuthorizedKnowledgeNotebookResult.Denied(); }
        if (!resolvedAudience.Granted || resolvedAudience.Grant!.CampaignId != request.CampaignId)
            return AuthorizedKnowledgeNotebookResult.Denied();

        var grant = resolvedAudience.Grant;
        try
        {
            var binding = await bindings.ResolveAsync(request.CampaignId, cancellationToken);
            if (binding is null || binding.CampaignEntityId != request.CampaignId)
                return AuthorizedKnowledgeNotebookResult.Denied();
            binding.Validate();
            var knownKinds = binding.KnowledgeKinds.Select(value => value.Kind)
                .ToHashSet(StringComparer.Ordinal);
            if (request.Kinds is { Count: > 0 } && request.Kinds.Any(value => !knownKinds.Contains(value)))
                return new("invalid", [], [], "INVALID_KNOWLEDGE_REQUEST");

            var scope = await source.ReadCampaignScopeAsync(binding, cancellationToken);
            if (scope is null || scope.CampaignId != request.CampaignId)
                return AuthorizedKnowledgeNotebookResult.Denied();

            var actor = grant.Role == KnowledgeAudienceRole.Actor;
            if (actor)
            {
                var active = await participation.ResolveAsync(binding, grant.ActorId!, cancellationToken);
                if (!active.Active || string.IsNullOrWhiteSpace(active.Revision))
                    return AuthorizedKnowledgeNotebookResult.Denied();
            }

            var projection = await source.ReadWorldAsync(binding, scope, cancellationToken);
            if (projection is null || projection.Scope != scope)
                return AuthorizedKnowledgeNotebookResult.Unavailable();

            IReadOnlyDictionary<string, EffectiveKnowledgeState> effective =
                new Dictionary<string, EffectiveKnowledgeState>(StringComparer.Ordinal);
            IReadOnlySet<string>? allowed = null;
            if (actor)
            {
                effective = await states.ResolveAllAsync(binding, grant.ActorId!, scope.WorldId,
                    projection.Documents.Select(value => value.KnowledgeId).ToArray(), cancellationToken);
                allowed = projection.Documents.Where(document => effective.TryGetValue(
                        document.KnowledgeId, out var state) && state.WorldId == scope.WorldId &&
                        (binding.ContentStates.Contains(state.State, StringComparer.Ordinal) ||
                         state.State == binding.FamiliarState))
                    .Select(document => document.KnowledgeId).ToHashSet(StringComparer.Ordinal);
            }

            IReadOnlyList<CanonicalKnowledgeDocument> selected;
            if (!string.IsNullOrWhiteSpace(request.Query))
            {
                selected = lexical.Search(projection.Documents, new(request.Query!, request.Kinds,
                    null, scope.CurrentMinute, request.Limit, allowed))
                    .Select(value => value.Document).ToArray();
            }
            else
            {
                selected = projection.Documents.Where(document =>
                        (allowed is null || allowed.Contains(document.KnowledgeId)) &&
                        !document.Archived &&
                        (document.ValidFromMinute is null || document.ValidFromMinute <= scope.CurrentMinute) &&
                        (document.ValidUntilMinute is null || document.ValidUntilMinute > scope.CurrentMinute) &&
                        (request.Kinds is not { Count: > 0 } ||
                         request.Kinds.Contains(document.Kind, StringComparer.Ordinal)))
                    .OrderBy(value => value.PresentationKind, StringComparer.Ordinal)
                    .ThenBy(value => value.DisplayText, StringComparer.OrdinalIgnoreCase)
                    .Take(request.Limit).ToArray();
            }

            var entries = new List<AuthorizedKnowledgeNotebookEntry>(selected.Count);
            var locationIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
            var locations = new List<(string Name, List<AuthorizedKnowledgeNotebookEntry> Entries)>();
            foreach (var document in selected)
            {
                var hydrated = await source.ReadDocumentAsync(
                    binding, scope.WorldId, document.KnowledgeId, cancellationToken);
                if (hydrated is null || hydrated.Revision != document.Revision) continue;

                var stance = binding.BaselineState;
                if (actor)
                {
                    var rechecked = await states.ResolveAllAsync(binding, grant.ActorId!, scope.WorldId,
                        [hydrated.KnowledgeId], cancellationToken);
                    if (!rechecked.TryGetValue(hydrated.KnowledgeId, out var current) ||
                        !effective.TryGetValue(hydrated.KnowledgeId, out var before) ||
                        current.Revision != before.Revision) continue;
                    stance = current.State;
                    if (stance != binding.FamiliarState &&
                        !binding.ContentStates.Contains(stance, StringComparer.Ordinal)) continue;
                }

                // Familiarity deliberately does not reveal the proposition text or its subject.
                var entry = stance == binding.FamiliarState
                    ? new AuthorizedKnowledgeNotebookEntry(
                        "You recognize this as a familiar topic, but do not remember details.",
                        stance, "recognition")
                    : new AuthorizedKnowledgeNotebookEntry(
                        hydrated.DisplayText, stance, hydrated.PresentationKind);
                entries.Add(entry);
                if (stance == binding.FamiliarState || !hydrated.SubjectIsActiveLocation) continue;

                if (!locationIndexes.TryGetValue(hydrated.SubjectId, out var locationIndex))
                {
                    locationIndex = locations.Count;
                    locationIndexes.Add(hydrated.SubjectId, locationIndex);
                    locations.Add((hydrated.SubjectName, []));
                }
                locations[locationIndex].Entries.Add(entry);
            }

            return new(entries.Count == 0 ? "empty" : "ready", entries.AsReadOnly(),
                locations.Select(location => new AuthorizedKnowledgeNotebookLocation(
                    location.Name, location.Entries.AsReadOnly())).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return AuthorizedKnowledgeNotebookResult.Unavailable();
        }
    }

    private static bool Valid(AuthorizedKnowledgeNotebookRequest? request) => request is not null &&
        Token(request.CampaignId, 200) && request.Limit is >= 1 and <= 200 &&
        (request.Query is null || request.Query.Length is >= 1 and <= 500 &&
            request.Query == request.Query.Trim()) &&
        (request.Kinds is null || request.Kinds.Count <= 16 &&
            request.Kinds.Distinct(StringComparer.Ordinal).Count() == request.Kinds.Count &&
            request.Kinds.All(value => Token(value, 200)));

    private static bool Token(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum &&
        !value.Any(char.IsWhiteSpace);
}
