using System.Security.Cryptography;
using System.Text.Json;
using DantesRoleplay.Actions;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Composes the action pipeline and owns its transaction. The MCP layer delegates here; it does
/// not select mechanics, interpret projections or write the world itself.
/// </summary>
public sealed class ActionRunner(
    DantesRoleplayDbContext db,
    IMechanicStore mechanics,
    IProjectionResolver projections,
    IMechanicEngine engine,
    IEffectApplier applier,
    DantesRoleplay.Operations.IOperationLog log,
    IMechanicComposer? composer = null) : IActionRunner, IStoryPlanActionRunner
{
    // The public verb this is served as, not the historical tool name. Everything else records
    // through ToolRunner, which stamps the protocol identity for it; this runner owns its own
    // audit rows because it owns the transaction, so it has to say so itself (VERB_MIGRATION.md
    // D10). Which rule ran is not lost — it is the subject, and mechanicId besides.
    private const string Tool = "commit";
    private readonly DantesRoleplayDbContext _db = db;
    private readonly IMechanicStore _mechanics = mechanics;
    private readonly IProjectionResolver _projections = projections;
    private readonly IMechanicEngine _engine = engine;
    private readonly IEffectApplier _applier = applier;
    private readonly DantesRoleplay.Operations.IOperationLog _log = log;
    private readonly IMechanicComposer? _composer = composer;

    public async Task<ActionRunResult> RunAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(request, null, cancellationToken);

    async Task<ActionRunResult> IStoryPlanActionRunner.RunWithParticipantAsync(
        ActionRequest request,
        IActionCommitParticipant participant,
        CancellationToken cancellationToken)
        => await ExecuteAsync(request, participant, cancellationToken);

    private async Task<ActionRunResult> ExecuteAsync(
        ActionRequest request,
        IActionCommitParticipant? participant,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestError = ValidateRequest(request);

        if (requestError is not null)
        {
            return await RecordFailureAsync(
                request,
                ActionRunResult.Failed(
                    requestError.Code,
                    requestError.Why,
                    requestError.Fix,
                    "Rejected run_action arguments."),
                null,
                null,
                null,
                CancellationToken.None);
        }

        IDbContextTransaction? transaction = null;
        MechanicSummary? selected = null;
        MechanicProjection? projection = null;
        long? seed = request.Seed;
        IReadOnlyList<MechanicSummary> candidates = [];

        try
        {
            transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            var found = await _mechanics.FindAsync(
                request.Intent,
                scope: request.Scope,
                includeInactive: true,
                limit: 50,
                cancellationToken: cancellationToken);

            candidates = found
                .Where(candidate => candidate.Status == MechanicStatus.Active)
                .ToList();

            if (candidates.Count == 0)
            {
                return await FailInTransactionAsync(
                    transaction,
                    request,
                    ActionRunResult.Failed(
                        "NO_ACTIVE_MECHANIC",
                        $"No active mechanic matched the intent '{request.Intent}'.",
                        "orient()",
                        $"No active mechanic matched '{request.Intent}'.",
                        found),
                    selected,
                    projection,
                    seed);
            }

            selected = candidates[0];
            var detail = await _mechanics.GetAsync(selected.Id, selected.Version, cancellationToken);

            if (detail is null || detail.Status != MechanicStatus.Active)
            {
                return await FailInTransactionAsync(
                    transaction,
                    request,
                    ActionRunResult.Failed(
                        "MECHANIC_UNAVAILABLE",
                        $"The selected mechanic '{selected.Id}' is no longer active.",
                        "orient()",
                        $"Selected mechanic '{selected.Id}' was unavailable.",
                        candidates),
                    selected,
                    projection,
                    seed);
            }

            seed ??= BitConverter.ToInt64(RandomNumberGenerator.GetBytes(sizeof(long)));

            MechanicRequirements requirements;

            try
            {
                requirements = MechanicRequirements.Parse(detail.Requirements);
            }
            catch (JsonException ex)
            {
                return await FailInTransactionAsync(
                    transaction,
                    request,
                    ActionRunResult.Failed(
                        "INVALID_REQUIREMENTS",
                        $"Mechanic '{selected.Id}' has invalid requirements JSON: {ex.Message}",
                        $"query(kind: \"mechanics\", id: \"{selected.Id}\")",
                        $"Mechanic '{selected.Id}' could not be projected.",
                        candidates),
                    selected,
                    projection,
                    seed);
            }

            var resolution = await _projections.ResolveAsync(
                requirements,
                request.RoleEntityIds,
                request.Input,
                seed.Value,
                cancellationToken);

            if (!resolution.Ok || resolution.Projection is null)
            {
                var problems = string.Join(
                    " ",
                    resolution.Problems.Select(problem =>
                        problem));

                return await FailInTransactionAsync(
                    transaction,
                    request,
                    ActionRunResult.Failed(
                        "PROJECTION_FAILED",
                        $"The mechanic projection was rejected. {problems}",
                        $"query(kind: \"mechanics\", id: \"{selected.Id}\") — read the roles it "
                        + "declares, then send the same action again with roleEntityIds filled in.",
                        $"Projection failed for '{selected.Id}'.",
                        candidates) with { Mechanic = selected, Seed = seed },
                    selected,
                    projection,
                    seed);
            }

            projection = resolution.Projection;
            var childProposal = CompositionProposal.Empty;

            if (requirements.Children.Count > 0)
            {
                if (_composer is null)
                {
                    return await FailInTransactionAsync(
                        transaction,
                        request,
                        ActionRunResult.Failed(
                            "COMPOSITION_UNAVAILABLE",
                            $"Mechanic '{selected.Id}' declares child mechanics, but this host has no composition service.",
                            "Restart the host with IMechanicComposer registered before running this mechanic.",
                            $"Composition was unavailable for '{selected.Id}'.",
                            candidates) with { Mechanic = selected, Projection = projection, Seed = seed },
                        selected,
                        projection,
                        seed);
                }

                var composition = await _composer.ComposeAsync(
                    detail.Id,
                    requirements,
                    projection,
                    cancellationToken: cancellationToken);

                if (!composition.Ok || composition.Projection is null)
                {
                    return await FailInTransactionAsync(
                        transaction,
                        request,
                        ActionRunResult.Failed(
                            "COMPOSITION_FAILED",
                            composition.Error,
                            $"query(kind: \"mechanics\", id: \"{selected.Id}\") — review its declared children and bindings.",
                            $"Composition failed for '{selected.Id}'.",
                            candidates) with { Mechanic = selected, Projection = projection, Seed = seed },
                        selected,
                        projection,
                        seed);
                }

                projection = composition.Projection;
                childProposal = composition.Proposal;
            }

            var run = await _engine.RunAsync(
                detail.Source,
                projection,
                ExecutionLimits.Default,
                cancellationToken);

            if (!run.Ok)
            {
                return await FailInTransactionAsync(
                    transaction,
                    request,
                    ActionRunResult.Failed(
                        string.IsNullOrWhiteSpace(run.LimitHit) ? "MECHANIC_FAILED" : "MECHANIC_LIMIT",
                        string.IsNullOrWhiteSpace(run.LimitHit)
                            ? run.Error
                            : $"{run.Error} Limit hit: {run.LimitHit}.",
                        $"query(kind: \"mechanics\", id: \"{selected.Id}\") — the rule is broken, "
                        + "not your arguments; read it, then revise it with "
                        + "commit(kind: \"mechanic\", ..., dryRun: true).",
                        $"Mechanic '{selected.Id}' failed.",
                        candidates) with
                    {
                        Mechanic = selected,
                        Projection = projection,
                        Seed = seed,
                        Log = run.Log,
                        LimitHit = run.LimitHit,
                        ElapsedMilliseconds = run.ElapsedMilliseconds
                    },
                    selected,
                    projection,
                    seed);
            }

            var output = MergeChildProposals(childProposal, run.Output);

            var dryRun = await _applier.ApplyAsync(
                output.Effects,
                dryRun: true,
                cancellationToken: cancellationToken);

            if (!dryRun.Valid)
            {
                return await FailInTransactionAsync(
                    transaction,
                    request,
                    ActionRunResult.Failed(
                        "INVALID_EFFECTS",
                        FormatProblems(dryRun.Problems),
                        "query(kind: \"procedures\", id: \"procedure.world.change\")",
                        $"Mechanic '{selected.Id}' proposed invalid effects.",
                        candidates) with
                    {
                        Mechanic = selected,
                        Projection = projection,
                        Output = output,
                        Seed = seed,
                        Log = run.Log,
                        LimitHit = run.LimitHit,
                        ElapsedMilliseconds = run.ElapsedMilliseconds
                    },
                    selected,
                    projection,
                    seed);
            }

            // Allocate the audit id before effects commit: structural events need the same id as
            // their correlation/root operation, and the operation row is written later in this
            // transaction after the action outcome is known.
            var operationId = DantesRoleplay.Operations.Operation.NewId();

            // Apply the exact list that just passed the dry run. EffectApplier detects the ambient
            // transaction and leaves commit/rollback ownership with this runner.
            var applied = await _applier.ApplyAsync(
                output.Effects,
                dryRun: false,
                cancellationToken: cancellationToken,
                rootOperationId: operationId,
                declaredEvents: output.Events);

            if (applied.Blocked)
            {
                var invalidDeclaredEvent = string.Equals(
                    applied.BlockCode,
                    "SUBSCRIBER_INVALID_EVENT",
                    StringComparison.Ordinal);

                return await FailInTransactionAsync(
                    transaction,
                    request,
                    ActionRunResult.Failed(
                        invalidDeclaredEvent ? "INVALID_DECLARED_EVENT" : "EVENT_BLOCKED",
                        invalidDeclaredEvent
                            ? $"The mechanic declared an invalid event: {applied.BlockReason}"
                            : $"A guard blocked the proposed world change: {applied.BlockCode}: {applied.BlockReason}",
                        invalidDeclaredEvent
                            ? "query(kind: \"event-types\")"
                            : "query(kind: \"subscriptions\")",
                        invalidDeclaredEvent
                            ? $"Mechanic '{selected.Id}' declared an invalid event."
                            : $"Mechanic '{selected.Id}' was blocked by a guard.",
                        candidates) with
                    {
                        Mechanic = selected,
                        Projection = projection,
                        Output = output,
                        Seed = seed,
                        Log = run.Log,
                        LimitHit = run.LimitHit,
                        ElapsedMilliseconds = run.ElapsedMilliseconds
                    },
                    selected,
                    projection,
                    seed);
            }

            if (!applied.Valid || !applied.Applied)
            {
                return await FailInTransactionAsync(
                    transaction,
                    request,
                    ActionRunResult.Failed(
                        "EFFECT_APPLICATION_FAILED",
                        FormatProblems(applied.Problems),
                        "query(kind: \"procedures\", id: \"procedure.world.change\")",
                        $"Mechanic '{selected.Id}' could not apply its effects.",
                        candidates) with
                    {
                        Mechanic = selected,
                        Projection = projection,
                        Output = output,
                        Seed = seed,
                        Log = run.Log,
                        LimitHit = run.LimitHit,
                        ElapsedMilliseconds = run.ElapsedMilliseconds
                    },
                    selected,
                    projection,
                    seed);
            }

            var affected = AffectedEntities(output.Effects);
            var subject = Subject(affected, selected.Id);
            var operation = await _log.RecordAsync(
                Tool,
                $"Ran {selected.Id} v{selected.Version}; applied {applied.Count} effect(s).",
                success: true,
                intent: request.Intent,
                subject: subject,
                proceduresCited: request.ProceduresUsed,
                consumesReadEvidence: true,
                cancellationToken: cancellationToken,
                mechanicId: selected.Id,
                mechanicVersion: selected.Version,
                seed: seed,
                projectionJson: Serialize(projection),
                id: operationId);

            var succeeded = new ActionRunResult
            {
                Ok = true,
                OperationId = operation.Id,
                Summary = $"Ran {selected.Id} v{selected.Version}; applied {applied.Count} effect(s).",
                Candidates = candidates,
                Mechanic = selected,
                Projection = projection,
                Output = output,
                Seed = seed,
                AppliedCount = applied.Count,
                AffectedEntityIds = affected,
                Log = run.Log,
                LimitHit = run.LimitHit,
                ElapsedMilliseconds = run.ElapsedMilliseconds,
                NextSteps = ConfirmationSteps(affected)
            };
            if (participant is not null) await participant.StageAsync(succeeded, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return succeeded;
        }
        catch (OperationCanceledException)
        {
            if (transaction is null)
            {
                return await RecordFailureAsync(
                    request,
                    CancelledFailure(candidates, selected, projection, seed),
                    selected,
                    projection,
                    seed,
                    CancellationToken.None);
            }

            return await FailInTransactionAsync(
                transaction,
                request,
                ActionRunResult.Failed(
                    "CANCELLED",
                    "The action was cancelled before it completed.",
                    "query(kind: \"history\", failuresOnly: true)",
                    "Action cancelled.",
                    candidates) with
                {
                    Mechanic = selected,
                    Projection = projection,
                    Seed = seed
                },
                selected,
                projection,
                seed);
        }
        catch (StoryPlanResultLimitException)
        {
            var failure = ActionRunResult.Failed(
                "STORY_INTERNAL_FAILURE",
                "The final story handoff exceeds the safe result limit.",
                "query(kind: \"history\", failuresOnly: true)",
                "Story receipt exceeded its safe result limit.",
                candidates) with
            {
                Mechanic = selected,
                Projection = projection,
                Seed = seed
            };
            if (transaction is null)
                return await RecordFailureAsync(request, failure, selected, projection, seed, CancellationToken.None);
            return await FailInTransactionAsync(transaction, request, failure, selected, projection, seed);
        }
        catch (Exception ex)
        {
            if (transaction is null)
            {
                return await RecordFailureAsync(
                    request,
                    ActionRunResult.Failed(
                        "UNHANDLED",
                        ex.Message,
                        "orient()",
                        "Unhandled run_action failure.",
                        candidates) with
                    {
                        Mechanic = selected,
                        Projection = projection,
                        Seed = seed
                    },
                    selected,
                    projection,
                    seed,
                    CancellationToken.None);
            }

            return await FailInTransactionAsync(
                transaction,
                request,
                ActionRunResult.Failed(
                    "UNHANDLED",
                    ex.Message,
                    "orient()",
                    "Unhandled run_action failure.",
                    candidates) with
                {
                    Mechanic = selected,
                    Projection = projection,
                    Seed = seed
                },
                selected,
                projection,
                seed);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<ActionRunResult> FailInTransactionAsync(
        IDbContextTransaction transaction,
        ActionRequest request,
        ActionRunResult failure,
        MechanicSummary? mechanic,
        MechanicProjection? projection,
        long? seed)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        _db.ChangeTracker.Clear();
        return await RecordFailureAsync(request, failure, mechanic, projection, seed, CancellationToken.None);
    }

    private async Task<ActionRunResult> RecordFailureAsync(
        ActionRequest request,
        ActionRunResult failure,
        MechanicSummary? mechanic,
        MechanicProjection? projection,
        long? seed,
        CancellationToken cancellationToken)
    {
        var operation = await _log.RecordAsync(
            Tool,
            failure.Summary,
            success: false,
            intent: request.Intent,
            subject: Subject(failure.AffectedEntityIds, mechanic?.Id ?? string.Empty),
            proceduresCited: request.ProceduresUsed,
            error: failure.Error?.Code ?? "UNHANDLED",
            consumesReadEvidence: true,
            cancellationToken: cancellationToken,
            mechanicId: mechanic?.Id ?? string.Empty,
            mechanicVersion: mechanic?.Version,
            seed: seed,
            projectionJson: projection is null ? string.Empty : Serialize(projection));

        return failure with { OperationId = operation.Id };
    }

    private static ActionRunResult CancelledFailure(
        IReadOnlyList<MechanicSummary> candidates,
        MechanicSummary? mechanic,
        MechanicProjection? projection,
        long? seed) =>
        ActionRunResult.Failed(
            "CANCELLED",
            "The action was cancelled before it completed.",
            "query(kind: \"history\", failuresOnly: true)",
            "Action cancelled.",
            candidates) with
        {
            Mechanic = mechanic,
            Projection = projection,
            Seed = seed
        };

    private static ActionRunError? ValidateRequest(ActionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Intent))
        {
            return new ActionRunError(
                "INVALID_INTENT",
                "An action intent is required.",
                "commit(kind: \"action\", payload: \"{\\\"intent\\\":\\\"describe what the actor is trying to do\\\",\\\"roleEntityIds\\\":{}}\")");
        }

        if (!ActionInput.TryValidateObject(request.Input, out var inputProblem))
        {
            return new ActionRunError(
                "INVALID_INPUT",
                inputProblem!,
                "commit(kind: \"action\", payload: \"{\\\"intent\\\":\\\"same intent\\\",\\\"roleEntityIds\\\":{},\\\"input\\\":\\\"{}\\\"}\")");
        }

        if (request.RoleEntityIds is null)
        {
            return new ActionRunError(
                "INVALID_ROLE_MAP",
                "The role-to-entity map cannot be null.",
                "commit(kind: \"action\", payload: \"{\\\"intent\\\":\\\"same intent\\\",\\\"roleEntityIds\\\":{}}\")");
        }

        if (request.RoleEntityIds.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
        {
            return new ActionRunError(
                "INVALID_ROLE_MAP",
                "Role names and entity ids must both be non-empty.",
                "commit(kind: \"action\", payload: \"{\\\"intent\\\":\\\"same intent\\\",\\\"roleEntityIds\\\":{\\\"subject\\\":\\\"entity-id\\\"}}\")");
        }

        return null;
    }

    private static MechanicOutput MergeChildProposals(
        CompositionProposal childProposal,
        MechanicOutput parentOutput) =>
        parentOutput with
        {
            Effects = [.. childProposal.Effects, .. parentOutput.Effects],
            Events = [.. childProposal.Events, .. parentOutput.Events],
            Notifications = [.. childProposal.Notifications, .. parentOutput.Notifications]
        };

    private static string FormatProblems(IReadOnlyList<EffectProblem> problems) =>
        problems.Count == 0
            ? "The effect applier rejected the action without a detailed problem."
            : $"{problems.Count} problem(s); nothing was applied. " +
              string.Join(" ", problems.Select(p => $"[{p.Index}] {p.Effect}: {p.Problem}"));

    private static IReadOnlyList<string> AffectedEntities(IReadOnlyList<Effect> effects) =>
        effects
            .SelectMany(effect => new[] { effect.EntityId, effect.ToEntityId })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string Subject(IReadOnlyList<string> affected, string fallback)
    {
        var subject = affected.Count == 0 ? fallback : string.Join(",", affected);
        return subject.Length <= 200 ? subject : subject[..200];
    }

    private static IReadOnlyList<string> ConfirmationSteps(IReadOnlyList<string> affected) =>
        affected.Count == 0
            ? ["query(kind: \"history\") — review the recorded action result."]
            : [$"query(kind: \"entities\", ids: [{string.Join(", ", affected.Select(id => $"\"{id}\""))}]) — confirm the applied world state."];

    /// <summary>
    /// Relaxed escaping, because this string is stored and then read back by a person or a model
    /// asking what a rule was handed. The default encoder writes every quote as \u0022, which is
    /// valid JSON and unreadable at a glance — and the point of keeping the projection is that
    /// someone can look at it.
    /// </summary>
    private static readonly JsonSerializerOptions ReadableJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string Serialize(MechanicProjection projection) =>
        JsonSerializer.Serialize(projection, ReadableJson);
}
