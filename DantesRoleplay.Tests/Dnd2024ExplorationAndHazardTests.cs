using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.Events;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Sources;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024ExplorationAndHazardTests : Dnd2024TestBase
{
    [Fact]
    public async Task Dice_primitive_is_seeded_bounded_closed_and_effect_free()
    {
        await using var harness = await DndHarness.CreateAsync();
        var noRoles = new Dictionary<string, string>();
        var first = await harness.EvaluateRolesAsync("dnd2024.mechanic.dice", noRoles,
            "{\"count\":2,\"sides\":6,\"modifier\":3}", 4242);
        var replay = await harness.EvaluateRolesAsync("dnd2024.mechanic.dice", noRoles,
            "{\"count\":2,\"sides\":6,\"modifier\":3}", 4242);
        var defaults = await harness.EvaluateRolesAsync("dnd2024.mechanic.dice", noRoles, "{}", 7);
        var invalid = await harness.EvaluateRolesAsync("dnd2024.mechanic.dice", noRoles,
            "{\"count\":101}", 7);
        var extra = await harness.EvaluateRolesAsync("dnd2024.mechanic.dice", noRoles,
            "{\"cheat\":20}", 7);

        Assert.True(first.Ok, first.Run?.Error);
        Assert.Equal(first.Run!.Output.Data, replay.Run!.Output.Data);
        using var data = JsonDocument.Parse(first.Run.Output.Data);
        var rolls = data.RootElement.GetProperty("rolls").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();
        Assert.Equal(2, rolls.Length);
        Assert.All(rolls, value => Assert.InRange(value, 1, 6));
        Assert.Contains("\"count\":1,\"sides\":20,\"modifier\":0", defaults.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.False(invalid.Ok);
        Assert.False(extra.Ok);
        Assert.Empty(first.Run.Output.Effects);
        Assert.Empty(first.Run.Output.Events);
        Assert.Empty(first.Run.Output.Notifications);
    }

    [Theory]
    [InlineData("fast", 1, 1, 96)]
    [InlineData("normal", 1, 1, 120)]
    [InlineData("slow", 1, 1, 160)]
    [InlineData("normal", 2, 1, 240)]
    [InlineData("normal", 1, 2, 240)]
    public async Task Dnd_route_profile_resolves_pace_terrain_and_visibility_into_exact_minutes(
        string pace, int terrainMultiplier, int visibilityMultiplier, int expectedMinutes)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddTravelFixturesAsync(
            terrainMultiplier: terrainMultiplier,
            visibilityMultiplier: visibilityMultiplier);

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.travel.execute", TravelRoles(), TravelInput(pace), 17,
            "41000000000000000000000000000001"));

        Assert.True(result.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            result.Disposition + ": " + string.Join("; ", result.Problems.Select(value =>
                value.Code + " " + value.SafeMessage)));
        using var clock = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "world.travel.fixture", "game.core.world.clock"))!.ValueJson);
        Assert.Equal(100 + expectedMinutes, clock.RootElement.GetProperty("currentMinute").GetInt32());
        Assert.Equal(8, clock.RootElement.GetProperty("revision").GetInt32());
    }

    [Fact]
    public async Task Prepared_route_travel_moves_records_exposure_events_and_replays_without_duplicates()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddTravelFixturesAsync(exposureCadenceMinutes: 60);
        var discovery = harness.Search("travel the party along the named route");
        var request = harness.ActionForRoles(
            "dnd2024.mechanic.travel.execute", TravelRoles(), TravelInput("normal"), 17,
            "42000000000000000000000000000001");

        var first = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);

        Assert.Equal("dnd2024.mechanic.travel.execute",
            Assert.Single(discovery.Records).Record.QualifiedId);
        Assert.True(first.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            first.Disposition + ": " + string.Join("; ", first.Problems.Select(value =>
                value.Code + " " + value.SafeMessage)));
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.NotNull(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "journey.slice9", "dnd2024.exploration.journey"));
        var schedule = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "exposure.slice9", "dnd2024.exploration.exposure-schedule");
        Assert.NotNull(schedule);
        using (var state = JsonDocument.Parse(schedule!.ValueJson))
            Assert.Equal(2, state.RootElement.GetProperty("occurrences").GetInt32());
        var containment = await harness.Edges.GetContainmentAsync(
            DndHarness.StateSpaceId, "subject.high");
        Assert.Equal("location.travel.destination", containment!.ContainerEntityId);
        Assert.Equal("presence", containment.Slot);
        Assert.Equal(3, (await harness.RuleEventsAsync(first.OperationId)).Count);
    }

    [Fact]
    public async Task Failed_verified_navigation_roll_commits_no_movement_journey_exposure_or_time()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddTravelFixturesAsync(navigationRequired: true, navigationDc: 100,
            exposureCadenceMinutes: 60);
        await harness.AddProficiencyStateAsync("subject.high", 1, []);
        var roles = TravelRoles();
        roles["navigator"] = "subject.high";
        var input = TravelInput("normal", "\"navigationCheck\":{\"ability\":\"wis\",\"skill\":\"survival\",\"dc\":100}");

        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.travel.execute", roles, input, 17,
            "43000000000000000000000000000001"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "journey.slice9", "dnd2024.exploration.journey"));
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "exposure.slice9", "dnd2024.exploration.exposure-schedule"));
        Assert.Equal("location.travel.origin", (await harness.Edges.GetContainmentAsync(
            DndHarness.StateSpaceId, "subject.high"))!.ContainerEntityId);
        using var clock = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "world.travel.fixture", "game.core.world.clock"))!.ValueJson);
        Assert.Equal(100, clock.RootElement.GetProperty("currentMinute").GetInt32());
        Assert.Empty(await harness.EventsAsync(failed.OperationId));
    }

    [Fact]
    public async Task Later_transaction_failure_rolls_back_the_complete_route_travel_proposal()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddTravelFixturesAsync(exposureCadenceMinutes: 60);

        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.travel.execute", TravelRoles(), TravelInput("normal"), 17,
            "44000000000000000000000000000001"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "journey.slice9", "dnd2024.exploration.journey"));
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "exposure.slice9", "dnd2024.exploration.exposure-schedule"));
        Assert.Equal("location.travel.origin", (await harness.Edges.GetContainmentAsync(
            DndHarness.StateSpaceId, "subject.high"))!.ContainerEntityId);
    }

    [Fact]
    public async Task Failed_trap_detection_reveals_no_hazard_identity_or_event()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddHazardFixturesAsync(detectionDc: 100);
        var input = "{\"check\":{\"ability\":\"wis\",\"skill\":\"perception\",\"dc\":100},\"expectedDefinitionRevision\":1}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.trap.detect", TrapDetectRoles(), input, 17,
            "51000000000000000000000000000001"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, result.Disposition);
        Assert.Equal("No hidden hazard is established.", result.Narration);
        Assert.DoesNotContain("hazard.trap", result.Narration, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            "subject.high", "hazard.trap.instance.fixture", "dnd2024.hazard.detected"));
        Assert.Empty(await harness.EventsAsync(result.OperationId));
        Assert.Contains(harness.Search("detect a hidden trap").Records,
            value => value.Record.QualifiedId == "dnd2024.mechanic.trap.detect");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(100, false)]
    public async Task Detected_trap_disarm_uses_the_exact_authored_check(
        int disarmDc, bool expectedDisarmed)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddHazardFixturesAsync(disarmDc: disarmDc);
        var detected = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.trap.detect", TrapDetectRoles(),
            "{\"check\":{\"ability\":\"wis\",\"skill\":\"perception\",\"dc\":0},\"expectedDefinitionRevision\":1}",
            17, "52000000000000000000000000000001"));
        var disarmInput = "{\"check\":{\"ability\":\"dex\",\"skill\":\"sleight-of-hand\",\"dc\":"
                          + disarmDc + "},\"expectedDefinitionRevision\":1}";

        var disarmed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.trap.disarm", TrapDisarmRoles(), disarmInput, 17,
            expectedDisarmed
                ? "52000000000000000000000000000002"
                : "52000000000000000000000000000003"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, detected.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, disarmed.Disposition);
        Assert.NotNull(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            "subject.high", "hazard.trap.instance.fixture", "dnd2024.hazard.detected"));
        using var state = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "hazard.trap.instance.fixture",
            "dnd2024.hazard.trap-state"))!.ValueJson);
        Assert.Equal(expectedDisarmed
                ? "dnd2024.hazard.trap-phase.disabled"
                : "dnd2024.hazard.trap-phase.armed",
            state.RootElement.GetProperty("phase").GetProperty("entityId").GetString());
        Assert.Equal(expectedDisarmed ? 1 : 0,
            (await harness.RuleEventsAsync(disarmed.OperationId)).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Trap_trigger_reset_clear_and_replay_preserve_one_authoritative_history(bool canonicalInput)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddHazardFixturesAsync();
        var triggerInput = "{\"expectedDefinitionRevision\":1,\"saveCheck\":{\"ability\":\"dex\",\"dc\":100},\"damageEffects\":[{\"amount\":3,\"damageType\":\"piercing\",\"saveSucceeded\":false,\"successfulSaveBehavior\":\"none\"}],\"conditionEffects\":[{\"mode\":\"apply\",\"conditions\":[\"prone\"]}]}";
        if (canonicalInput)
            triggerInput = DantesRoleplay.Interactions.InteractionCanonicalJson.CanonicalizeObject(triggerInput);
        var trigger = harness.ActionForRoles(
            "dnd2024.mechanic.trap.trigger", TrapTriggerRoles(),
            triggerInput,
            17, "53000000000000000000000000000001");

        var first = await harness.Runner.RunAsync(trigger);
        var replay = await harness.Runner.RunAsync(trigger);

        Assert.True(first.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            first.Disposition + ": " + string.Join("; ", first.Problems.Select(value =>
                value.Code + " " + value.SafeMessage)));
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        using (var hitPoints = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.hit-points"))!.ValueJson))
            Assert.Equal(7, hitPoints.RootElement.GetProperty("current").GetInt32());
        using (var conditions = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "subject.high", "dnd2024.conditions"))!.ValueJson))
            Assert.Contains(conditions.RootElement.GetProperty("entries").EnumerateArray(),
                value => value.GetProperty("condition").GetString() == "prone");
        Assert.Single(await harness.RuleEventsAsync(first.OperationId));

        var reset = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.trap.reset", TrapLifecycleRoles(),
            "{\"expectedDefinitionRevision\":1,\"satisfiedResetKind\":\"dawn\"}", 0,
            "53000000000000000000000000000002"));
        var cleared = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.trap.clear", TrapLifecycleRoles(),
            "{\"expectedDefinitionRevision\":1}", 0,
            "53000000000000000000000000000003"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, reset.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, cleared.Disposition);
        using var finalState = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "hazard.trap.instance.fixture",
            "dnd2024.hazard.trap-state"))!.ValueJson);
        Assert.Equal("dnd2024.hazard.trap-phase.cleared",
            finalState.RootElement.GetProperty("phase").GetProperty("entityId").GetString());
        Assert.Equal(1, finalState.RootElement.GetProperty("activationCount").GetInt32());
    }

    [Fact]
    public async Task Environmental_exposure_progresses_time_supports_mitigation_and_recovery()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddHazardFixturesAsync();
        var begin = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.environment-exposure.begin", ExposureBeginRoles(),
            "{\"exposureId\":\"hazard.exposure.fixture\",\"expectedDefinitionRevision\":1}", 0,
            "54000000000000000000000000000001"));
        var progressRequest = harness.ActionForRoles(
            "dnd2024.mechanic.environment-exposure.progress", ExposureProgressRoles("hazard.exposure.fixture"),
            FailedExposureInput(7), 17, "54000000000000000000000000000002");
        var preview = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.environment-exposure.resolve",
            ExposureResolveRoles("hazard.exposure.fixture"), FailedExposureInput(7), 17);

        Assert.True(preview.Ok, preview.Run?.Error ?? string.Join("; ", preview.Problems));

        var progress = await harness.Runner.RunAsync(progressRequest);
        var replay = await harness.Runner.RunAsync(progressRequest);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, begin.Disposition);
        Assert.True(progress.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            progress.Disposition + ": " + string.Join("; ", progress.Problems.Select(value =>
                value.Code + " " + value.SafeMessage)));
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        using (var exposure = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "hazard.exposure.fixture",
                   "dnd2024.hazard.environment-exposure"))!.ValueJson))
        {
            Assert.Equal(60, exposure.RootElement.GetProperty("accumulatedExposure")
                .GetProperty("value").GetProperty("numerator").GetInt32());
            Assert.Equal(1, exposure.RootElement.GetProperty("exposureCount").GetInt32());
            Assert.Equal(1, exposure.RootElement.GetProperty("failedChecks").GetInt32());
        }
        using (var clock = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "world.hazard.fixture", "game.core.world.clock"))!.ValueJson))
        {
            Assert.Equal(160, clock.RootElement.GetProperty("currentMinute").GetInt32());
            Assert.Equal(8, clock.RootElement.GetProperty("revision").GetInt32());
        }
        Assert.Equal(2, (await harness.RuleEventsAsync(progress.OperationId)).Count);

        var recovered = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.environment-exposure.recover", ExposureRecoverRoles("hazard.exposure.fixture"),
            "{\"expectedDefinitionRevision\":1}", 0,
            "54000000000000000000000000000003"));
        var afterRecovery = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.environment-exposure.progress", ExposureProgressRoles("hazard.exposure.fixture"),
            FailedExposureInput(8), 17, "54000000000000000000000000000004"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recovered.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, afterRecovery.Disposition);

        await harness.AddExposureRecordAsync("hazard.exposure.mitigated.fixture");
        var mitigated = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.environment-exposure.progress",
            ExposureProgressRoles("hazard.exposure.mitigated.fixture"),
            "{\"minutes\":60,\"expectedClockRevision\":8,\"expectedDefinitionRevision\":1,\"selectedMitigationIndex\":0,\"damageEffects\":[],\"conditionEffects\":[]}",
            17, "54000000000000000000000000000005"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, mitigated.Disposition);
        using var hitPoints = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.hit-points"))!.ValueJson);
        Assert.Equal(8, hitPoints.RootElement.GetProperty("current").GetInt32());
    }

    [Fact]
    public async Task Environmental_progress_transaction_failure_rolls_back_consequences_events_and_clock()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddHazardFixturesAsync();
        await harness.AddExposureRecordAsync();

        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.environment-exposure.progress", ExposureProgressRoles("hazard.exposure.fixture"),
            FailedExposureInput(7), 17, "55000000000000000000000000000001"));

        Assert.True(failed.Disposition == ApplicationActionExecutionDisposition.Failed,
            failed.Disposition + ": " + string.Join("; ", failed.Problems.Select(value =>
                value.Code + " " + value.SafeMessage)));
        using (var hitPoints = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.hit-points"))!.ValueJson))
            Assert.Equal(10, hitPoints.RootElement.GetProperty("current").GetInt32());
        using (var conditions = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "subject.high", "dnd2024.conditions"))!.ValueJson))
            Assert.Empty(conditions.RootElement.GetProperty("entries").EnumerateArray());
        using (var exposure = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "hazard.exposure.fixture",
                   "dnd2024.hazard.environment-exposure"))!.ValueJson))
            Assert.Equal(0, exposure.RootElement.GetProperty("exposureCount").GetInt32());
        using (var clock = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "world.hazard.fixture", "game.core.world.clock"))!.ValueJson))
            Assert.Equal(100, clock.RootElement.GetProperty("currentMinute").GetInt32());
        Assert.Empty(await harness.EventsAsync(failed.OperationId));
    }

    [Fact]
    public async Task Immediate_poison_resolves_authored_failed_and_successful_saves_once()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddAfflictionFixturesAsync();
        var failedSave = harness.ActionForRoles(
            "dnd2024.mechanic.poison.apply", PoisonApplyRoles("hazard.poison.immediate.fixture", "activity.poison.save.fail"),
            PoisonApplyInput("affliction.poison.immediate.fixture", 100,
                "[{\"amount\":2,\"damageType\":\"poison\",\"saveSucceeded\":false,\"successfulSaveBehavior\":\"none\"}]",
                "[{\"mode\":\"apply\",\"conditions\":[\"poisoned\"]}]"),
            17, "56000000000000000000000000000001");

        var applied = await harness.Runner.RunAsync(failedSave);
        var replay = await harness.Runner.RunAsync(failedSave);
        var avoided = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.poison.apply", PoisonApplyRoles("hazard.poison.success.fixture", "activity.poison.save.success"),
            PoisonApplyInput("affliction.poison.success.fixture", 0,
                "[{\"amount\":2,\"damageType\":\"poison\",\"saveSucceeded\":true,\"successfulSaveBehavior\":\"none\"}]", "[]"),
            17, "56000000000000000000000000000002"));

        AssertSucceeded(applied);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        AssertSucceeded(avoided);
        using (var hp = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.hit-points"))!.ValueJson))
            Assert.Equal(8, hp.RootElement.GetProperty("current").GetInt32());
        using (var conditions = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "subject.high", "dnd2024.conditions"))!.ValueJson))
            Assert.Single(conditions.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Single(await harness.RuleEventsAsync(applied.OperationId));
        Assert.Contains(harness.Search("apply an authored poison").Records,
            value => value.Record.QualifiedId == "dnd2024.mechanic.poison.apply");
    }

    [Fact]
    public async Task Delayed_poison_waits_for_authoritative_time_then_progresses_and_recovers()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddAfflictionFixturesAsync();
        var roles = PoisonApplyRoles("hazard.poison.delayed.fixture", null);
        var applied = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.poison.apply", roles,
            PoisonApplyInput("affliction.poison.delayed.fixture", null, "[]", "[]"),
            17, "57000000000000000000000000000001"));
        var premature = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.poison.progress", PoisonProgressRoles("affliction.poison.delayed.fixture"),
            PoisonProgressInput(7), 17, "57000000000000000000000000000002"));

        await harness.SetAfflictionClockAsync(105, 8);
        var progressed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.poison.progress", PoisonProgressRoles("affliction.poison.delayed.fixture"),
            PoisonProgressInput(8), 17, "57000000000000000000000000000003"));
        var recovered = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.poison.recover", PoisonRecoverRoles("affliction.poison.delayed.fixture"),
            AfflictionCheckInput(0), 17, "57000000000000000000000000000004"));

        AssertSucceeded(applied);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, premature.Disposition);
        AssertSucceeded(progressed);
        AssertSucceeded(recovered);
        Assert.NotNull(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            "affliction.poison.delayed.fixture", "subject.high", "dnd2024.affliction.recovered"));
        using var clock = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "world.affliction.fixture", "game.core.world.clock"))!.ValueJson);
        Assert.Equal(105, clock.RootElement.GetProperty("currentMinute").GetInt32());
    }

    [Fact]
    public async Task Contagion_requires_explicit_exposure_and_source_bound_transmission()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddAfflictionFixturesAsync();
        var exposed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.contagion.expose", ContagionExposeRoles(),
            ContagionStartInput("affliction.contagion.source.fixture", 7), 0,
            "58000000000000000000000000000001"));
        await harness.SetAfflictionClockAsync(105, 8);
        var progressed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.contagion.progress", ContagionProgressRoles("affliction.contagion.source.fixture"),
            "{\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\"" + Fingerprint
            + "\",\"expectedClockRevision\":8,\"damageEffects\":[{\"amount\":1,\"damageType\":\"poison\",\"saveSucceeded\":false,\"successfulSaveBehavior\":\"full\"}],\"conditionEffects\":[{\"mode\":\"apply\",\"conditions\":[\"poisoned\"]}]}",
            0, "58000000000000000000000000000002"));
        var transmitted = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.contagion.transmit", ContagionTransmitRoles(),
            ContagionStartInput("affliction.contagion.target.fixture", 8), 0,
            "58000000000000000000000000000003"));
        var recovered = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.contagion.recover", ContagionRecoverRoles(),
            AfflictionCheckInput(0), 17, "58000000000000000000000000000004"));

        AssertSucceeded(exposed);
        AssertSucceeded(progressed);
        AssertSucceeded(transmitted);
        AssertSucceeded(recovered);
        Assert.NotNull(await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "affliction.contagion.target.fixture", "dnd2024.hazard.contagion-application"));
        Assert.NotNull(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            "affliction.contagion.target.fixture", "affliction.contagion.source.fixture",
            "dnd2024.contagion.source-application"));
    }

    [Fact]
    public async Task Curse_knowledge_is_observer_specific_and_separate_from_inventory_source()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddAfflictionFixturesAsync();
        var bound = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.curse.bind", CurseBindRoles(),
            "{\"applicationId\":\"affliction.curse.fixture\",\"appliedAtEventId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\""
            + Fingerprint + "\",\"expectedClockRevision\":7,\"bindingEvent\":\"curse.fixture.bound\"}",
            0, "59000000000000000000000000000001"));
        var discovered = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.curse.discover", CurseDiscoverRoles(),
            CurseDiscoveryInput(), 17, "59000000000000000000000000000002"));
        var removed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.curse.remove", CurseRemoveRoles(),
            AfflictionCheckInput(0), 17, "59000000000000000000000000000003"));

        AssertSucceeded(bound);
        Assert.DoesNotContain("Fixture Curse", bound.Narration, StringComparison.Ordinal);
        Assert.Null(await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "item.curse.source.fixture", "dnd2024.hazard.curse"));
        AssertSucceeded(discovered);
        Assert.NotNull(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            "affliction.curse.fixture", "subject.low", "dnd2024.curse.known-by"));
        Assert.NotNull(await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "knowledge.curse.fixture", "dnd2024.magic-item.knowledge"));
        Assert.NotNull(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            "subject.low", "item.curse.source.fixture", "dnd2024.magic-item.knowledge"));
        Assert.Null(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            "affliction.curse.fixture", "subject.high", "dnd2024.curse.known-by"));
        AssertSucceeded(removed);
        Assert.NotNull(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            "affliction.curse.fixture", "subject.high", "dnd2024.affliction.recovered"));
    }

    [Fact]
    public async Task Affliction_transaction_failure_rolls_back_application_consequences_and_event()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddAfflictionFixturesAsync();
        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.poison.apply", PoisonApplyRoles("hazard.poison.immediate.fixture", "activity.poison.save.fail"),
            PoisonApplyInput("affliction.poison.rollback.fixture", 100,
                "[{\"amount\":2,\"damageType\":\"poison\",\"saveSucceeded\":false,\"successfulSaveBehavior\":\"none\"}]",
                "[{\"mode\":\"apply\",\"conditions\":[\"poisoned\"]}]"),
            17, "5a000000000000000000000000000001"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            "affliction.poison.rollback.fixture"));
        using var hp = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.hit-points"))!.ValueJson);
        Assert.Equal(10, hp.RootElement.GetProperty("current").GetInt32());
        Assert.Empty(await harness.EventsAsync(failed.OperationId));
    }

    [Fact]
    public async Task Object_durability_resolves_threshold_responses_repairs_and_registered_read_model()
    {
        await using var harness = await DndHarness.CreateAsync();
        await AddObjectDurabilityFixturesAsync(harness);
        var roles = ObjectDurabilityRoles("object.durability.fixture", "object.definition.fixture");
        var initialized = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.object.durability.initialize", roles,
            ObjectVersionInput(), 0, "5b000000000000000000000000000001"));
        AssertSucceeded(initialized);

        var threshold = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.object.damage", roles,
            ObjectDamageInput(4, "bludgeoning"), 0,
            "5b000000000000000000000000000002"));
        var resistant = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.object.damage", roles,
            ObjectDamageInput(10, "fire"), 0,
            "5b000000000000000000000000000003"));
        var immune = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.object.damage", roles,
            ObjectDamageInput(100, "cold"), 0,
            "5b000000000000000000000000000004"));
        var vulnerable = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.object.damage", roles,
            ObjectDamageInput(4, "acid"), 0,
            "5b000000000000000000000000000005"));
        AssertSucceeded(threshold);
        AssertSucceeded(resistant);
        AssertSucceeded(immune);
        AssertSucceeded(vulnerable);

        using (var state = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "object.durability.fixture",
                   "dnd2024.object.durability"))!.ValueJson))
        {
            Assert.Equal(7, state.RootElement.GetProperty("currentHitPoints").GetInt32());
            Assert.False(state.RootElement.GetProperty("destroyed").GetBoolean());
        }

        var partial = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.object.repair", roles,
            ObjectRepairInput(5, "cost-free", "[]"), 0,
            "5b000000000000000000000000000006"));
        var fullRequest = harness.ActionForRoles(
            "dnd2024.mechanic.object.repair", roles,
            ObjectRepairInput(100, "cost-free", "[]"), 0,
            "5b000000000000000000000000000007");
        var full = await harness.Runner.RunAsync(fullRequest);
        var replay = await harness.Runner.RunAsync(fullRequest);
        AssertSucceeded(partial);
        AssertSucceeded(full);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);

        var read = await harness.ReadModels.ReadAsync(new(
            DndHarness.StateSpaceId, ApplicationIdentifier.Parse("dnd2024"),
            "dnd2024.query.object-durability", roles));
        using var projected = JsonDocument.Parse(read.DataJson);
        Assert.Equal(20, projected.RootElement.GetProperty("durability")
            .GetProperty("currentHitPoints").GetInt32());
        Assert.True(projected.RootElement.GetProperty("usable").GetBoolean());
        Assert.Equal("dnd2024.mechanic.object.damage", projected.RootElement
            .GetProperty("nextActions")[0].GetProperty("capabilityId").GetString());
        Assert.Matches("^[0-9A-F]{64}$", read.OutputSchemaHash);
        Assert.Contains(harness.Search("show object damage and repair status").Records,
            value => value.Record.QualifiedId == "dnd2024.mechanic.object.durability.read");
    }

    [Fact]
    public async Task Zero_hit_points_destroy_owned_capabilities_atomically_and_replay_once()
    {
        await using var harness = await DndHarness.CreateAsync();
        await AddObjectDurabilityFixturesAsync(harness);
        var roles = ObjectDurabilityRoles("object.durability.fixture", "object.definition.fixture");
        AssertSucceeded(await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.object.durability.initialize", roles,
            ObjectVersionInput(), 0, "5c000000000000000000000000000001")));
        var input = "{\"amount\":25,\"damageType\":\"bludgeoning\","
                    + "\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\"" + Fingerprint + "\","
                    + "\"destruction\":{\"expectedBeforeCurrent\":20,\"postDamageCurrent\":0,"
                    + "\"damageType\":\"bludgeoning\",\"resolvedDamage\":25,\"expectedDefinitionRevision\":1,"
                    + "\"expectedDefinitionFingerprint\":\"" + Fingerprint + "\",\"specialDestructionSatisfied\":false,"
                    + "\"consequences\":[\"unequip\",\"close-route\",\"disable-trap\"]}}";
        var request = harness.ActionForRoles("dnd2024.mechanic.object.damage", roles, input, 0,
            "5c000000000000000000000000000002");

        var destroyed = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);

        AssertSucceeded(destroyed);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        using (var state = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "object.durability.fixture",
                   "dnd2024.object.durability"))!.ValueJson))
            Assert.True(state.RootElement.GetProperty("destroyed").GetBoolean());
        Assert.Null(await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "object.durability.fixture", "dnd2024.item.equipment"));
        using (var route = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "object.durability.fixture",
                   "game.core.world.route.availability"))!.ValueJson))
            Assert.Equal("closed", route.RootElement.GetProperty("status").GetString());
        using (var trap = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "object.durability.fixture",
                   "dnd2024.hazard.trap-state"))!.ValueJson))
            Assert.Equal("dnd2024.hazard.trap-phase.disabled",
                trap.RootElement.GetProperty("phase").GetProperty("entityId").GetString());
        Assert.Equal(2, (await harness.RuleEventsAsync(destroyed.OperationId)).Count);

        var ineligible = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.object.repair", roles,
            ObjectRepairInput(5, "cost-free", "[]"), 0,
            "5c000000000000000000000000000003"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, ineligible.Disposition);
        var rebuilt = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.object.repair", roles,
            ObjectRepairInput(5, "slice14-settled",
                "[\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"]"), 0,
            "5c000000000000000000000000000004"));
        AssertSucceeded(rebuilt);
    }

    [Fact]
    public async Task Magic_item_resilience_and_stabilization_fail_closed_until_explicitly_satisfied()
    {
        await using var harness = await DndHarness.CreateAsync();
        await AddObjectDurabilityFixturesAsync(harness);
        var roles = ObjectDurabilityRoles("object.resilient.fixture", "object.resilient.definition.fixture");
        AssertSucceeded(await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.object.durability.initialize", roles,
            ObjectVersionInput(), 0, "5d000000000000000000000000000001")));
        var damaged = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.object.damage", roles,
            ObjectDamageInput(20, "force"), 0,
            "5d000000000000000000000000000002"));
        AssertSucceeded(damaged);
        using (var state = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "object.resilient.fixture",
                   "dnd2024.object.durability"))!.ValueJson))
        {
            Assert.Equal(0, state.RootElement.GetProperty("currentHitPoints").GetInt32());
            Assert.False(state.RootElement.GetProperty("destroyed").GetBoolean());
        }
        var destroyed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.object.destroy", roles,
            "{\"expectedBeforeCurrent\":0,\"postDamageCurrent\":0,\"damageType\":\"force\",\"resolvedDamage\":20,"
            + "\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\"" + Fingerprint + "\","
            + "\"specialDestructionSatisfied\":true,\"consequences\":[]}", 0,
            "5d000000000000000000000000000003"));
        AssertSucceeded(destroyed);

        await harness.AddApplicationComponentAsync("object.durability.fixture",
            "dnd2024.object.durability",
            "{\"currentHitPoints\":10,\"destroyed\":false,\"stabilized\":false,\"basisRevision\":1,"
            + "\"basisFingerprint\":\"" + Fingerprint + "\",\"activeDamageEffects\":[{\"entityId\":\"effect.fixture\"}]}");
        var stabilized = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.object.stabilize",
            ObjectDurabilityRoles("object.durability.fixture", "object.definition.fixture"),
            ObjectVersionInput(), 0, "5d000000000000000000000000000004"));
        AssertSucceeded(stabilized);
    }

    [Fact]
    public async Task Object_destruction_transaction_failure_rolls_back_state_consequences_and_events()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await AddObjectDurabilityFixturesAsync(harness, initialized: true);
        var roles = ObjectDurabilityRoles("object.durability.fixture", "object.definition.fixture");
        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.object.damage", roles,
            "{\"amount\":25,\"damageType\":\"bludgeoning\",\"expectedDefinitionRevision\":1,"
            + "\"expectedDefinitionFingerprint\":\"" + Fingerprint + "\",\"destruction\":{"
            + "\"expectedBeforeCurrent\":20,\"postDamageCurrent\":0,\"damageType\":\"bludgeoning\","
            + "\"resolvedDamage\":25,\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\""
            + Fingerprint + "\",\"specialDestructionSatisfied\":false,"
            + "\"consequences\":[\"unequip\",\"close-route\",\"disable-trap\"]}}",
            0, "5e000000000000000000000000000001"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        using var state = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "object.durability.fixture",
            "dnd2024.object.durability"))!.ValueJson);
        Assert.Equal(20, state.RootElement.GetProperty("currentHitPoints").GetInt32());
        Assert.False(state.RootElement.GetProperty("destroyed").GetBoolean());
        Assert.NotNull(await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "object.durability.fixture", "dnd2024.item.equipment"));
        Assert.Empty(await harness.EventsAsync(failed.OperationId));
    }

    [Fact]
    public async Task Destroyed_items_cannot_be_equipped_and_destroyed_travellers_cannot_travel()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string definitionId = "dnd2024.item.destroyed-fixture.v1";
        await harness.AddItemDefinitionAsync(definitionId, "Destroyed fixture definition",
            SeparateItemDefinition("[\"held\"]"));
        await harness.AddPhysicalItemAsync("item.destroyed.fixture", "Destroyed fixture",
            definitionId, "subject.high");
        await harness.AddApplicationComponentAsync("item.destroyed.fixture",
            "dnd2024.object.durability",
            "{\"currentHitPoints\":0,\"destroyed\":true,\"stabilized\":true}");
        var equip = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item.equip", new Dictionary<string, string>
            {
                ["item"] = "item.destroyed.fixture", ["holder"] = "subject.high"
            }, "{\"slotIds\":[\"dnd2024.equipment-slot.main-hand\"]}", 0,
            "5f000000000000000000000000000001"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, equip.Disposition);

        await harness.AddTravelFixturesAsync();
        await harness.AddApplicationComponentAsync("subject.high", "dnd2024.object.durability",
            "{\"currentHitPoints\":0,\"destroyed\":true,\"stabilized\":true}");
        var travel = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.travel.execute", TravelRoles(), TravelInput("normal"), 0,
            "5f000000000000000000000000000002"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, travel.Disposition);
        using var clock = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "world.travel.fixture", "game.core.world.clock"))!.ValueJson);
        Assert.Equal(100, clock.RootElement.GetProperty("currentMinute").GetInt32());
    }
}
