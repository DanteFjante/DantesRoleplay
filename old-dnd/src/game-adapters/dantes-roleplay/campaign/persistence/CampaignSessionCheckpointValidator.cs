using DantesRoleplay.Campaign;
using DantesRoleplay.World;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// S4 Slice 1's zero-effect gate for one named ended-session evidence checkpoint. It consumes the
/// S3 historical owner and rejects pre-existing or malformed checkpoint graph state; it neither
/// produces/stages package bytes nor creates checkpoint state.
/// </summary>
public sealed class CampaignSessionCheckpointValidator(
    IWorldStore world,
    ICampaignSessionRecapReader recaps) : ICampaignSessionCheckpointValidator
{
    private const string CheckpointLink = "game.core.campaign.session.has-checkpoint";
    private readonly IWorldStore _world = world;
    private readonly ICampaignSessionRecapReader _recaps = recaps;

    public async Task<CampaignSessionCheckpointValidationResult> ValidateAsync(
        CampaignSessionCheckpointRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.Operation != "validate-session-checkpoint"
            || !Id(request.SessionId, "session.") || request.ExpectedStatus != "ended")
        {
            return Failure(request?.SessionId, null, "INVALID_SESSION_CHECKPOINT_REQUEST", "payload",
                "Checkpoint validation requires operation validate-session-checkpoint, one canonical session.* id, and expectedStatus ended.",
                "commit(kind: \"campaign\", payload: \"{\\\"operation\\\":\\\"validate-session-checkpoint\\\",\\\"sessionId\\\":\\\"session....\\\",\\\"expectedStatus\\\":\\\"ended\\\"}\")");
        }

        var historical = await _recaps.GetAsync(request.SessionId, cancellationToken);
        if (!historical.Found)
        {
            return Failure(request.SessionId, historical.CampaignId, "SESSION_CHECKPOINT_REQUIRES_ENDED_SESSION", "sessionId",
                "A checkpoint requires one valid ended S3 session with its immutable factual recap.",
                "query(kind: \"session-recap\", id: \"...\")");
        }

        var links = (await _world.GetRelationshipsAsync(request.SessionId, true, cancellationToken))
            .Where(link => link.Kind == CheckpointLink)
            .ToArray();
        if (links.Length == 0)
        {
            return new("valid", request.SessionId, historical.CampaignId, [],
                $"commit(kind: \"campaign\", payload: \"{{\\\"operation\\\":\\\"checkpoint-session\\\",\\\"sessionId\\\":\\\"{request.SessionId}\\\",\\\"expectedStatus\\\":\\\"ended\\\"}}\")");
        }

        if (links.Any(link => link.FromEntityId != request.SessionId || !Id(link.ToEntityId, "checkpoint.") || link.Data != "{}"))
        {
            return Failure(request.SessionId, historical.CampaignId, "SESSION_CHECKPOINT_SCOPE_INVALID", "sessionId",
                "Checkpoint links must be empty-data directed links from this session to canonical checkpoint entities.",
                "query(kind: \"entities\", id: \"...\")");
        }

        foreach (var link in links)
        {
            if (await _world.GetEntityAsync(link.ToEntityId, cancellationToken) is null)
            {
                return Failure(request.SessionId, historical.CampaignId, "SESSION_CHECKPOINT_SCOPE_INVALID", "sessionId",
                    "Checkpoint links must not name a missing or deleted checkpoint entity.",
                    "query(kind: \"entities\", id: \"...\")");
            }
        }

        return Failure(request.SessionId, historical.CampaignId, "SESSION_CHECKPOINT_ALREADY_EXISTS", "sessionId",
            "The ended session already has a named checkpoint and S4 permits only one.",
            $"query(kind: \"entities\", id: \"{links[0].ToEntityId}\")");
    }

    private static CampaignSessionCheckpointValidationResult Failure(
        string? sessionId,
        string? campaignId,
        string code,
        string path,
        string reason,
        string recovery) => new("invalid", sessionId ?? string.Empty, campaignId,
        [new CampaignSessionProblem(code, path, reason, recovery)], recovery);

    private static bool Id(string? value, string prefix) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value == value.Trim()
        && value.StartsWith(prefix, StringComparison.Ordinal)
        && value.All(character => char.IsLower(character) || char.IsDigit(character) || character is '.' or '-');
}
