using DantesRoleplay.Actions;

namespace DantesRoleplay.DataAccess;

/// <summary>Data-access-only hook invoked inside ActionRunner's existing transaction.</summary>
internal interface IActionCommitParticipant
{
    Task StageAsync(ActionRunResult result, CancellationToken cancellationToken);
}

/// <summary>Internal companion to IActionRunner. It never crosses the kernel or MCP boundary.</summary>
internal interface IStoryPlanActionRunner
{
    Task<ActionRunResult> RunWithParticipantAsync(
        ActionRequest request,
        IActionCommitParticipant participant,
        CancellationToken cancellationToken = default);
}

/// <summary>Signals that a public story receipt would exceed its fixed safe envelope.</summary>
internal sealed class StoryPlanResultLimitException : Exception
{
    public StoryPlanResultLimitException() : base("The final story handoff exceeds the safe result limit.") { }
}
