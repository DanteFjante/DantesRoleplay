using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.ApplicationExecution;

/// <summary>Translation-only application effect builder shared by actions and event reactions.</summary>
public sealed class ApplicationEcsEffectBatchBuilder(
    IApplicationComponentTypeRegistry componentTypes,
    IEntityComponentStore entities,
    IStateSpaceEdgeStore edges) : IApplicationEcsEffectBatchBuilder
{
    private static readonly JsonSerializerOptions ProjectionJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<ApplicationEcsEffectBatchBuildResult> BuildAsync(
        StateSpaceView stateSpace,
        ApplicationMechanicProjectionMapping mapping,
        MechanicProjection projection,
        MechanicRequirements requirements,
        CompositionProposal proposal,
        string mechanicId,
        int mechanicVersion,
        long seed,
        CancellationToken cancellationToken = default)
    {
        var translated = await ApplicationActionRunner.TranslateAsync(stateSpace, mapping, projection,
            proposal.Effects, componentTypes, entities, edges, cancellationToken);
        if (translated.Problems.Count > 0)
            return new(null, translated.Problems, translated.Stale);
        var clocks = translated.Effects.Count(value => value.Type == ApplicationEcsEffectType.ClockAdvance);
        var mode = requirements.ElapsedTime?.Mode?.Trim();
        if (clocks > 1)
            return Failed("CLOCK_ADVANCE_MULTIPLE", "One application mechanic may advance the authoritative clock only once.");
        if (clocks == 1 && mode is not ("fixed" or "derived" or "supplied"))
            return Failed("ELAPSED_TIME_CONTRACT_MISSING", "A time-coupled mechanic must declare how its elapsed time is obtained.");
        if (clocks == 0 && mode is "fixed" or "derived" or "supplied")
            return Failed("CLOCK_ADVANCE_MISSING", "A non-zero elapsed-time declaration must produce one authoritative clock advance.");

        return new(new ApplicationEcsEffectBatch
        {
            StateSpaceId = stateSpace.StateSpaceId,
            Effects = translated.Effects,
            Intent = "Execute one verified application mechanic.",
            ProceduresUsed = [],
            ComponentExpectations = projection.ObservedComponents.Select(value =>
                new ApplicationEcsComponentExpectation(value.EntityId,
                    new EcsComponentReference(value.QualifiedTypeId, value.TypeVersion, value.SchemaHash), value.Revision)).ToArray(),
            EntityExpectations = projection.ObservedEntities.Select(value =>
                new ApplicationEcsEntityExpectation(value.EntityId, value.Revision)).ToArray(),
            RelationshipExpectations = projection.RelationshipCollections.Select(value =>
                new ApplicationEcsRelationshipExpectation(value.QualifiedKind, value.AnchorEntityId, value.Incoming,
                    value.Relationships.Select(edge => new ApplicationEcsRelationshipExpectationItem(
                        edge.FromEntityId, edge.ToEntityId, edge.Revision)).ToArray())).ToArray(),
            ContainmentExpectations = projection.ContainmentRevisions.OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => new ApplicationEcsContainmentExpectation(value.Key,
                    value.Value.Select(item => new EcsContainmentExpectationItem(item.EntityId, item.Slot, item.Revision)).ToArray())).ToArray(),
            DeclaredEvents = proposal.Events,
            MechanicId = mechanicId,
            MechanicVersion = mechanicVersion,
            Seed = seed,
            ProjectionJson = JsonSerializer.Serialize(projection, ProjectionJson)
        }, [], false);
    }

    private static ApplicationEcsEffectBatchBuildResult Failed(string code, string message) =>
        new(null, [new ApplicationActionExecutionProblem(code, message)], false);
}
