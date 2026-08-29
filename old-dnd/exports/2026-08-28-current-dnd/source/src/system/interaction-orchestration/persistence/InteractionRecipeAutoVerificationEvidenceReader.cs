using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

public sealed class InteractionRecipeAutoVerificationEvidenceReader(DantesRoleplayDbContext db)
    : IInteractionRecipeAutoVerificationEvidenceReader
{
    public async Task<InteractionRecipeAutoVerificationEligibility> ValidateAsync(
        InteractionRecipeAutoVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var supplied = request.ExecutionReceipt;
        if (supplied.Kind != "execution" || supplied.Status != "succeeded"
            || supplied.ResolutionReceiptId is null || supplied.ProposalFingerprint is null)
            return Ineligible("The learning receipt is not a completely successful execution.");

        var execution = await db.InteractionExecutionReceipts.AsNoTracking().Include(row => row.Steps)
            .SingleOrDefaultAsync(row => row.Id == supplied.Id
                && row.ResolutionReceiptId == supplied.ResolutionReceiptId, cancellationToken);
        var outer = await db.InteractionResolutionReceipts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == supplied.ResolutionReceiptId, cancellationToken);
        if (execution is null || outer is null || execution.Disposition != "succeeded"
            || outer.Status != "resolved" || outer.RoleProfile != InteractionRoleProfile.Outer.StableKey
            || outer.ProposalFingerprint is null
            || execution.ProposalFingerprint != outer.ProposalFingerprint
            || supplied.ProposalFingerprint != outer.ProposalFingerprint
            || execution.ApplicationId != outer.ApplicationId
            || execution.PrincipalReference != outer.PrincipalReference
            || execution.StateSpaceId != outer.StateSpaceId
            || execution.Steps.Count == 0
            || execution.Steps.Any(step => step.Disposition is not ("succeeded" or "replayed")))
            return Ineligible("The outer resolution and execution provenance is not complete.");

        const string suffix = ".outer";
        if (!outer.IdempotencyKey.EndsWith(suffix, StringComparison.Ordinal))
            return Ineligible("The outer resolution is not a host-correlated fallback attempt.");
        var innerKey = outer.IdempotencyKey[..^suffix.Length] + ".inner";
        var innerMatches = await db.InteractionResolutionReceipts.AsNoTracking().Where(row =>
                row.IdempotencyKey == innerKey
                && row.PrincipalReference == outer.PrincipalReference
                && row.ApplicationId == outer.ApplicationId
                && row.StateSpaceId == outer.StateSpaceId
                && row.SessionContextId == outer.SessionContextId
                && row.ConversationId == outer.ConversationId
                && row.ParentDelegationId == outer.ParentDelegationId
                && row.RoleProfile == InteractionRoleProfile.Inner.StableKey
                && row.CreatedAtUtc <= outer.CreatedAtUtc)
            .Take(2).ToArrayAsync(cancellationToken);
        if (innerMatches.Length != 1
            || innerMatches[0].Status is not ("unknown" or "unsupported" or "unavailable"))
            return Ineligible("No unique eligible inner non-resolution precedes the outer fallback.");

        var operationIds = execution.Steps.Select(step => step.OperationId).ToArray();
        if (operationIds.Any(string.IsNullOrWhiteSpace))
            return Failed("RECIPE_AUTO_VERIFICATION_FAILED",
                "A successful outer step has no operation audit link.");
        var distinct = operationIds.Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        var operationCount = await db.Operations.AsNoTracking()
            .CountAsync(row => distinct.Contains(row.Id), cancellationToken);
        if (operationCount != distinct.Length)
            return Failed("RECIPE_AUTO_VERIFICATION_FAILED",
                "An outer-fallback operation audit link is unavailable.");

        return new(true, "RECIPE_AUTO_VERIFICATION_ELIGIBLE",
            "The durable receipts prove one completely successful correlated outer fallback.");
    }

    private static InteractionRecipeAutoVerificationEligibility Ineligible(string summary) =>
        Failed("RECIPE_AUTO_VERIFICATION_INELIGIBLE", summary);

    private static InteractionRecipeAutoVerificationEligibility Failed(string code, string summary) =>
        new(false, code, summary);
}
