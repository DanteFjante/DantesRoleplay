using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;
using DantesRoleplay.Retrieval;

namespace DantesRoleplay.Actions;

public sealed record LocalRouteProposalRequest(
    string Intent,
    IReadOnlyDictionary<string, string>? RoleEntityIds = null,
    string Input = "{}",
    string? Scope = null,
    int CandidateLimit = 8);

public sealed record LocalActionProposal(
    string Kind,
    string MechanicId,
    string Intent,
    IReadOnlyDictionary<string, string> RoleEntityIds,
    string Input,
    string? Scope,
    IReadOnlyList<string> ProceduresUsed);

public sealed record LocalRouteProposalResult(
    string Status,
    string Confidence,
    string Reason,
    IReadOnlyList<MechanicSummary> MechanicCandidates,
    IReadOnlyList<ProcedureSummary> ProcedureCandidates,
    LocalActionProposal? Proposal,
    IReadOnlyList<string> MissingInformation,
    LocalModelIdentity? Model = null,
    long ElapsedMilliseconds = 0,
    int PromptTokens = 0,
    int OutputTokens = 0,
    string FallbackCode = "",
    string FallbackMessage = "",
    string ErrorCode = "",
    string ErrorMessage = "")
{
    public bool Ok => ErrorCode.Length == 0;

    public static LocalRouteProposalResult Fail(string code, string message) =>
        new("invalid", "none", "", [], [], null, [], ErrorCode: code, ErrorMessage: message);
}

/// <summary>Mode C proposes one existing action payload. It never invokes the action runner.</summary>
public interface ILocalRouteProposalCoordinator
{
    Task<LocalRouteProposalResult> ProposeAsync(
        LocalRouteProposalRequest request,
        CancellationToken cancellationToken = default);
}
