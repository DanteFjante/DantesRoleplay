namespace DantesRoleplay.Campaign;

/// <summary>
/// Closed S4 evidence-only readiness request. Capture remains a later slice and owns the outer
/// transaction; validation never creates a package or checkpoint.
/// </summary>
public sealed record CampaignSessionCheckpointRequest(string Operation, string SessionId, string ExpectedStatus);

public sealed record CampaignSessionCheckpointValidationResult(
    string Status,
    string SessionId,
    string? CampaignId,
    IReadOnlyList<CampaignSessionProblem> Problems,
    string Next)
{
    public bool Valid => Status == "valid";
}

public interface ICampaignSessionCheckpointValidator
{
    Task<CampaignSessionCheckpointValidationResult> ValidateAsync(
        CampaignSessionCheckpointRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record CampaignSessionCheckpointResult(string Status, string SessionId, string? CampaignId, string? WorldId, string? CheckpointId, int? ScopeContractVersion, string? Availability, string OperationId, IReadOnlyList<CampaignSessionProblem> Problems, string Next)
{ public bool Created => Status == "created"; }

public interface ICampaignSessionCheckpointCreator
{
    Task<CampaignSessionCheckpointResult> CreateAsync(CampaignSessionCheckpointRequest request, string intent = "", IReadOnlyList<string>? proceduresUsed = null, CancellationToken cancellationToken = default);
}
