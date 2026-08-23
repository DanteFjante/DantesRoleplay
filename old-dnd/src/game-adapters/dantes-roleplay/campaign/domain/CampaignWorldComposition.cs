using DantesRoleplay.Effects;
using DantesRoleplay.World;

namespace DantesRoleplay.Campaign;

/// <summary>Closed C10 campaign content; its World identity and references are derived from W17.</summary>
public sealed record NewWorldCampaignBlueprint(
    string CampaignId,
    string Title,
    string Premise,
    IReadOnlyList<string> PartyGoals,
    IReadOnlyList<string> ToneAndBoundaries,
    string RulesetScope,
    CampaignChapter InitialChapter,
    CampaignArc InitialArc);

public sealed record CampaignCompositionResult(
    string Status,
    CampaignBlueprint? Blueprint,
    IReadOnlyList<CampaignReferenceEvidence> ResolvedReferences,
    CampaignCreationCounts? Counts,
    IReadOnlyList<Effect> Effects,
    IReadOnlyList<CampaignProblem> Problems)
{
    public bool Valid => Status == "valid" && Blueprint is not null;
}

/// <summary>Effect-free C2 child used only by the later C10 coordinator.</summary>
public interface ICampaignCompositionAdapter
{
    Task<CampaignCompositionResult> ComposeAsync(
        NewWorldCampaignBlueprint blueprint,
        SmallWorldCompositionResult world,
        CancellationToken cancellationToken = default);
}
