using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.DataAccess;

public sealed class InteractionRecipeProvenanceReader(DantesRoleplayDbContext db) : IInteractionRecipeProvenanceReader
{
    public async Task<InteractionRecipeProvenanceValidation> ValidateAsync(
        InteractionRecipeProjection recipe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var evidence = recipe.Provenance?.Where(value => value.Kind == "derived").ToArray() ?? [];
        if (evidence.Length == 0)
            return Invalid("RECIPE_PROVENANCE_MISSING", "The recipe has no successful learning provenance.");
        foreach (var item in evidence)
        {
            var resolution = await db.InteractionResolutionReceipts.AsNoTracking().SingleOrDefaultAsync(row =>
                row.Id == item.ResolutionReceiptId && row.ApplicationId == recipe.ApplicationId.Value,
                cancellationToken);
            var execution = await db.InteractionExecutionReceipts.AsNoTracking().Include(row => row.Steps)
                .SingleOrDefaultAsync(row => row.Id == item.ExecutionReceiptId
                    && row.ResolutionReceiptId == item.ResolutionReceiptId
                    && row.ApplicationId == recipe.ApplicationId.Value, cancellationToken);
            if (resolution is null || resolution.Status != "resolved" || execution is null
                || execution.Disposition != "succeeded" || execution.ProposalFingerprint != resolution.ProposalFingerprint
                || execution.Steps.Count == 0 || execution.Steps.Any(step => step.Disposition != "succeeded"))
                return Invalid("RECIPE_PROVENANCE_INVALID", "A learning receipt is missing or not completely successful.");
            var operationIds = execution.Steps.Select(step => step.OperationId).ToArray();
            if (operationIds.Any(string.IsNullOrWhiteSpace))
                return Invalid("RECIPE_OPERATION_PROVENANCE_MISSING", "A successful recipe step has no operation audit link.");
            var distinct = operationIds.Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
            var found = await db.Operations.AsNoTracking().CountAsync(row => distinct.Contains(row.Id), cancellationToken);
            if (found != distinct.Length)
                return Invalid("RECIPE_OPERATION_PROVENANCE_INVALID", "A recipe operation audit link is unavailable.");
        }
        return new(true, "RECIPE_PROVENANCE_VALID", "All learning provenance is current and successful.");
    }

    private static InteractionRecipeProvenanceValidation Invalid(string code, string summary) => new(false, code, summary);
}
