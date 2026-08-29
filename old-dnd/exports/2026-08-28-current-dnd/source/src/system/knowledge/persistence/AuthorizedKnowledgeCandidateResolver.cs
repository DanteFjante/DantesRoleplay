namespace DantesRoleplay.Knowledge;

/// <summary>
/// Composes ambient authorization, application binding, campaign scope, actor participation,
/// effective state, and lexical retrieval. The audience policy is always the first dependency
/// called for a well-formed request.
/// </summary>
public sealed class AuthorizedKnowledgeCandidateResolver(
    IAuthorizedKnowledgeAudiencePolicy audience,
    IKnowledgeApplicationBindingResolver bindings,
    IKnowledgeActorParticipationVerifier participation,
    IKnowledgeCanonicalSource source,
    IKnowledgeEffectiveStateResolver states,
    IKnowledgeLexicalRetriever lexical) : IAuthorizedKnowledgeCandidateResolver
{
    public async Task<AuthorizedKnowledgeCandidateSet> ResolveAsync(
        AuthorizedKnowledgeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!AuthorizedKnowledgeRequestValidation.Valid(request))
            return new(false, false, "", "", [], false, "INVALID_KNOWLEDGE_REQUEST");

        KnowledgeAudienceResolution resolvedAudience;
        try { resolvedAudience = await audience.ResolveAsync(request.CampaignId, cancellationToken); }
        catch { return AuthorizedKnowledgeCandidateSet.Denied(); }
        if (!resolvedAudience.Granted || resolvedAudience.Grant!.CampaignId != request.CampaignId)
            return AuthorizedKnowledgeCandidateSet.Denied();

        var grant = resolvedAudience.Grant;
        try
        {
            var binding = await bindings.ResolveAsync(request.CampaignId, cancellationToken);
            if (binding is null || binding.CampaignEntityId != request.CampaignId)
                return AuthorizedKnowledgeCandidateSet.Denied();
            binding.Validate();
            var knownKinds = binding.KnowledgeKinds.Select(value => value.Kind)
                .ToHashSet(StringComparer.Ordinal);
            if (request.Kinds is { Count: > 0 } && request.Kinds.Any(value => !knownKinds.Contains(value)))
                return new(true, grant.Role == KnowledgeAudienceRole.Actor, grant.PolicyRevision,
                    "", [], false, "INVALID_KNOWLEDGE_REQUEST");

            var scope = await source.ReadCampaignScopeAsync(binding, cancellationToken);
            if (scope is null || scope.CampaignId != request.CampaignId)
                return AuthorizedKnowledgeCandidateSet.Denied();
            var actor = grant.Role == KnowledgeAudienceRole.Actor;
            var participationRevision = "game-master";
            if (actor)
            {
                var active = await participation.ResolveAsync(binding, grant.ActorId!, cancellationToken);
                if (!active.Active || string.IsNullOrWhiteSpace(active.Revision))
                    return AuthorizedKnowledgeCandidateSet.Denied();
                participationRevision = active.Revision;
            }

            var projection = await source.ReadWorldAsync(binding, scope, cancellationToken);
            if (projection is null || projection.Scope != scope)
                return new(true, actor, grant.PolicyRevision, "", [], false, "KNOWLEDGE_UNAVAILABLE");
            var minute = request.AsOfMinute ?? scope.CurrentMinute;
            if (minute > scope.CurrentMinute)
                return new(true, actor, grant.PolicyRevision, "", [], false, "INVALID_KNOWLEDGE_REQUEST");

            IReadOnlyDictionary<string, EffectiveKnowledgeState> effective =
                new Dictionary<string, EffectiveKnowledgeState>(StringComparer.Ordinal);
            IReadOnlySet<string>? allowed = null;
            IReadOnlySet<string> familiar = new HashSet<string>(StringComparer.Ordinal);
            if (actor)
            {
                effective = await states.ResolveAllAsync(binding, grant.ActorId!, scope.WorldId,
                    projection.Documents.Select(value => value.KnowledgeId).ToArray(), cancellationToken);
                allowed = projection.Documents.Where(document => effective.TryGetValue(
                        document.KnowledgeId, out var state) && state.WorldId == scope.WorldId &&
                        binding.ContentStates.Contains(state.State, StringComparer.Ordinal))
                    .Select(document => document.KnowledgeId).ToHashSet(StringComparer.Ordinal);
                familiar = projection.Documents.Where(document => effective.TryGetValue(
                        document.KnowledgeId, out var state) && state.WorldId == scope.WorldId &&
                        state.State == binding.FamiliarState)
                    .Select(document => document.KnowledgeId).ToHashSet(StringComparer.Ordinal);
            }

            var hits = lexical.Search(projection.Documents, new(request.Question, request.Kinds,
                request.SubjectIds, minute, request.CandidateLimit, allowed));
            var candidates = new List<AuthorizedKnowledgeCandidate>();
            foreach (var hit in hits)
            {
                var hydrated = await source.ReadDocumentAsync(
                    binding, scope.WorldId, hit.Document.KnowledgeId, cancellationToken);
                if (hydrated is null || hydrated.Revision != hit.Document.Revision) continue;
                EffectiveKnowledgeState? state = null;
                if (actor)
                {
                    var rechecked = await states.ResolveAllAsync(binding, grant.ActorId!, scope.WorldId,
                        [hydrated.KnowledgeId], cancellationToken);
                    if (!rechecked.TryGetValue(hydrated.KnowledgeId, out state) ||
                        !effective.TryGetValue(hydrated.KnowledgeId, out var before) ||
                        state.Revision != before.Revision ||
                        !binding.ContentStates.Contains(state.State, StringComparer.Ordinal)) continue;
                }
                candidates.Add(new(hydrated.KnowledgeId, hydrated.DisplayText,
                    actor ? state!.State : binding.BaselineState, hydrated.PresentationKind,
                    ApplicationKnowledgeCanonicalSource.Hash(new
                    {
                        hydrated.Revision,
                        StateRevision = state?.Revision ?? "game-master"
                    })));
            }

            var familiarMatch = false;
            if (actor && candidates.Count == 0 && familiar.Count > 0)
            {
                familiarMatch = lexical.Search(projection.Documents, new(request.Question,
                    request.Kinds, request.SubjectIds, minute, 1, familiar)).Count > 0;
            }
            var scopeRevision = ApplicationKnowledgeCanonicalSource.Hash(new
            {
                Binding = binding,
                ScopeRevision = scope.Revision,
                ProjectionRevision = projection.Revision,
                Participation = participationRevision
            });
            return new(true, actor, grant.PolicyRevision, scopeRevision, candidates,
                familiarMatch, "");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(true, grant.Role == KnowledgeAudienceRole.Actor, grant.PolicyRevision,
                "", [], false, "KNOWLEDGE_UNAVAILABLE");
        }
    }
}

internal static class AuthorizedKnowledgeRequestValidation
{
    internal static bool Valid(AuthorizedKnowledgeRequest? request) => request is not null &&
        Token(request.CampaignId, 200) && Text(request.Question, 500) &&
        request.CandidateLimit is >= 1 and <= 12 &&
        request.AsOfMinute is null or >= 0 and <= 1_000_000_000 &&
        ValidTokens(request.Kinds, 16) && ValidTokens(request.SubjectIds, 100);

    private static bool ValidTokens(IReadOnlyList<string>? values, int maximum) =>
        values is null || values.Count <= maximum && values.All(value => Token(value, 200));
    private static bool Token(string? value, int maximum) =>
        Text(value, maximum) && !value!.Any(char.IsWhiteSpace);
    private static bool Text(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
}
