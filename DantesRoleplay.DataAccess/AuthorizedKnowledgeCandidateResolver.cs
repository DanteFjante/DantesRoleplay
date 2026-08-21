using System.Text.Json;
using DantesRoleplay.Campaign;
using DantesRoleplay.Retrieval;
using DantesRoleplay.Security;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Resolves only the records an authenticated campaign audience may use. The policy is evaluated
/// before campaign or world state is read, and actor requests use lexical FTS with an allowlist
/// enforced before ranking and LIMIT. Vector retrieval is deliberately absent here because the
/// current vec0 boundary cannot apply this allowlist before its nearest-neighbour cut.
/// </summary>
public sealed class AuthorizedKnowledgeCandidateResolver(
    IAuthenticatedCampaignAudiencePolicy audiencePolicy,
    ICampaignCharacterParticipationVerifier participation,
    IWorldStore world,
    IKnowledgeSearchDocumentSource documents,
    IKnowledgeStateCoordinator knowledgeStates,
    IKnowledgeLexicalSearchCoordinator lexical) : IAuthorizedKnowledgeCandidateResolver
{
    private const string CampaignRoot = "game.core.campaign.root";
    private const string CampaignInWorld = "game.core.campaign.in-world";
    private static readonly IReadOnlySet<string> ContentStates = new HashSet<string>(StringComparer.Ordinal)
    {
        "known", "suspected", "believed", "doubted", "disbelieved"
    };

    private readonly IAuthenticatedCampaignAudiencePolicy _audiencePolicy = audiencePolicy;
    private readonly ICampaignCharacterParticipationVerifier _participation = participation;
    private readonly IWorldStore _world = world;
    private readonly IKnowledgeSearchDocumentSource _documents = documents;
    private readonly IKnowledgeStateCoordinator _knowledgeStates = knowledgeStates;
    private readonly IKnowledgeLexicalSearchCoordinator _lexical = lexical;

    public async Task<AuthorizedKnowledgeCandidateSet> ResolveAsync(
        AuthorizedKnowledgeAnswerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Valid(request)) return new(false, false, "", [], false, "INVALID_KNOWLEDGE_REQUEST");

        // This call must stay first. In particular, do not look up campaign existence before it.
        AuthenticatedCampaignAudienceResolution audience;
        try { audience = await _audiencePolicy.ResolveAsync(request.CampaignId, cancellationToken); }
        catch { return AuthorizedKnowledgeCandidateSet.Denied(); }
        if (!audience.Granted || audience.Grant!.CampaignId != request.CampaignId)
            return AuthorizedKnowledgeCandidateSet.Denied();

        try
        {
            var grant = audience.Grant;
            if (!await ActiveCampaignAsync(request.CampaignId, cancellationToken))
                return AuthorizedKnowledgeCandidateSet.Denied();
            var worldId = await ResolveWorldAsync(request.CampaignId, cancellationToken);
            if (worldId is null) return AuthorizedKnowledgeCandidateSet.Denied();

            var actor = grant.Role == CampaignAudienceRoles.Actor;
            if (actor)
            {
                var scope = await _participation.ResolveActiveScopeAsync(grant.ActorId!, cancellationToken);
                if (!scope.Valid || scope.CampaignId != request.CampaignId)
                    return AuthorizedKnowledgeCandidateSet.Denied();
            }

            var all = await _documents.ReadWorldAsync(worldId, cancellationToken);
            var states = new Dictionary<string, EffectiveKnowledgeState>(StringComparer.Ordinal);
            var allowed = new List<string>();
            var familiar = new List<string>();
            if (actor)
            {
                foreach (var document in all)
                {
                    var resolved = await _knowledgeStates.ResolveAsync(grant.ActorId!, document.KnowledgeId, cancellationToken);
                    if (!resolved.Resolved || resolved.Value!.WorldId != worldId) continue; // fail closed per record
                    states[document.KnowledgeId] = resolved.Value;
                    if (ContentStates.Contains(resolved.Value.State)) allowed.Add(document.KnowledgeId);
                    else if (resolved.Value.State == "familiar") familiar.Add(document.KnowledgeId);
                }
            }

            // The FTS projection is derived and may be empty on a newly started local host. Keep
            // this player-safe route self-contained until write-side background enqueueing owns
            // incremental synchronization. This never changes canonical world state.
            await _lexical.RebuildWorldAsync(worldId, cancellationToken);
            var searched = await _lexical.SearchAsync(new(
                worldId, request.Question, request.Kinds, request.SubjectIds, false,
                request.AsOfMinute, request.CandidateLimit, actor ? allowed : null), cancellationToken);
            if (!searched.Ok) return new(true, actor, grant.PolicyRevision, [], false, "KNOWLEDGE_UNAVAILABLE");

            var candidates = new List<AuthorizedKnowledgeCandidate>();
            foreach (var hit in searched.Hits)
            {
                var document = await _documents.ReadAsync(hit.KnowledgeId, cancellationToken);
                if (document is null || document.WorldId != worldId) continue;
                EffectiveKnowledgeState? state = null;
                if (actor)
                {
                    // Recheck after FTS/hydration: a stale projection or changed state never leaks.
                    var rechecked = await _knowledgeStates.ResolveAsync(grant.ActorId!, document.KnowledgeId, cancellationToken);
                    if (!rechecked.Resolved || rechecked.Value!.WorldId != worldId || !ContentStates.Contains(rechecked.Value.State)) continue;
                    state = rechecked.Value;
                }
                candidates.Add(new(
                    document.KnowledgeId,
                    SafeDisplayText(document.Text),
                    actor ? state!.State : "known",
                    Presentation(document.Kind),
                    Revision(document, state)));
            }

            var familiarMatch = false;
            if (actor && candidates.Count == 0 && familiar.Count > 0)
            {
                var recognition = await _lexical.SearchAsync(new(
                    worldId, request.Question, request.Kinds, request.SubjectIds, false,
                    request.AsOfMinute, 1, familiar), cancellationToken);
                familiarMatch = recognition.Ok && recognition.Hits.Count > 0;
            }
            return new(true, actor, grant.PolicyRevision, candidates, familiarMatch, "");
        }
        catch
        {
            // The public coordinator maps this to a generic unavailable/unknown response.
            return new(true, audience.Grant.Role == CampaignAudienceRoles.Actor, audience.Grant.PolicyRevision, [], false, "KNOWLEDGE_UNAVAILABLE");
        }
    }

    private async Task<bool> ActiveCampaignAsync(string campaignId, CancellationToken cancellationToken)
    {
        var campaign = await _world.GetEntityAsync(campaignId, cancellationToken);
        var root = campaign?.Components.Where(component => component.DefinitionId == CampaignRoot).ToArray();
        return root is { Length: 1 } && Active(root[0].Data);
    }

    private async Task<string?> ResolveWorldAsync(string campaignId, CancellationToken cancellationToken)
    {
        var links = (await _world.GetRelationshipsAsync(campaignId, false, cancellationToken))
            .Where(link => link.Kind == CampaignInWorld && Empty(link.Data) && Id(link.ToEntityId))
            .ToArray();
        return links.Length == 1 ? links[0].ToEntityId : null;
    }

    private static string Presentation(string kind) => kind switch
    {
        "rumour" => "rumour",
        "clue" => "evidence",
        _ => "statement" // secret content is never labelled as a secret to the actor.
    };

    private static string SafeDisplayText(string text)
    {
        // Search documents append canonical entity ids on later lines. Do not pass those ids to
        // the model, because even a well-behaved model could otherwise echo one in display text.
        var display = string.Join('\n', text.Split('\n', StringSplitOptions.None).Take(3)
            .Select(line => line.Trim()).Where(line => line.Length > 0));
        return display.Length <= 1_500 ? display : display[..1_500];
    }

    private static string Revision(KnowledgeLexicalDocument document, EffectiveKnowledgeState? state) =>
        state is null ? document.ContentHash : string.Join('|', document.ContentHash, state.State, state.SourceKind, state.SourceEntityId ?? "");

    private static bool Valid(AuthorizedKnowledgeAnswerRequest? request) => request is not null &&
        Id(request.CampaignId) && Text(request.Question, 500) && request.CandidateLimit is >= 1 and <= 12 &&
        request.AsOfMinute is null or >= 0 and <= 1_000_000_000 &&
        ValidIds(request.Kinds, 4) && ValidIds(request.SubjectIds, 20);
    private static bool ValidIds(IReadOnlyList<string>? values, int maximum) => values is null || values.Count <= maximum && values.All(Id);
    private static bool Id(string? value) => Text(value, 200);
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= maximum;
    private static bool Empty(string json) { try { using var document = JsonDocument.Parse(json); return document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.EnumerateObject().Any(); } catch { return false; } }
    private static bool Active(string json) { try { using var document = JsonDocument.Parse(json); return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == "active"; } catch { return false; } }
}
