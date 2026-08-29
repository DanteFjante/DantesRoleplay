namespace DantesRoleplay.Characters;

/// <summary>A stable, actionable reason an internal character-composition input was rejected.</summary>
public sealed record CharacterAbilityAssignmentProblem(string Code, string Path, string Reason, string Recovery);

/// <summary>
/// A CH5-supplied policy binding and the raw six-score object to validate. The policy entity is
/// resolved by the composition root; it is never selected by a public caller.
/// </summary>
public sealed record CharacterAbilityAssignmentValidationRequest(string BoundPolicyEntityId, string ScoresJson);

/// <summary>
/// The zero-effect result of validating raw ability allocation. A later CH2 recorder consumes
/// CanonicalScoresJson; this validator never creates actor state or opens a transaction.
/// </summary>
public sealed record CharacterAbilityAssignmentValidationPlan(
    string Status,
    string PolicyEntityId,
    int? PolicyVersion,
    string? CanonicalScoresJson,
    IReadOnlyList<CharacterAbilityAssignmentProblem> Problems)
{
    public bool Valid => Status == "valid";
}

public interface ICharacterAbilityAssignmentValidator
{
    Task<CharacterAbilityAssignmentValidationPlan> ValidateAsync(
        CharacterAbilityAssignmentValidationRequest request,
        CancellationToken cancellationToken = default);
}
